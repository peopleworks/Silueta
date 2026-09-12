using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Silueta.Core;

namespace Silueta.Mcp.Tools;

/// <summary>
/// De-identification, by file path.
/// <para>
/// The path is the whole design, and it follows from one fact about the protocol: <b>the arguments of a
/// tool call are written by the model.</b> A tool shaped <c>redact(text)</c> requires the model to have
/// read the transcript in order to pass it, so by the time the redactor runs, the identified text is
/// already in the context window, in the client's history, and in whatever the API provider logs. The
/// tool can return clean text. It cannot un-expose its own input.
/// </para>
/// <para>
/// So the model passes a path it never opened, this server reads the file itself, and what comes back is
/// the redacted text plus a manifest — offsets and counts, never the values. That is the same rule
/// <see cref="Detection"/> follows inside the library, carried out to the protocol boundary.
/// </para>
/// </summary>
[McpServerToolType]
public static class RedactionTools
{
    [McpServerTool(Name = "redact_transcript", ReadOnly = false),
     Description("""
        De-identifies a transcript ON DISK and returns the redacted text plus a manifest of what was
        removed. PREFER THIS over redact_text whenever the transcript contains real people: you pass a
        path, this server reads the file, and the identified text never enters your context.

        Needs a roster: a JSON array of { "value", "kind", "subjectId" } naming who this record is about
        (the patient, the family, the staff on shift). Silueta matches those through the spelling damage
        a speech recogniser leaves behind. Without a roster only the pattern rules fire (phone, e-mail,
        dates, ages over 89) and every name survives.

        Pass vaultPath on every run of one corpus, or each transcript invents different names for the
        same people. The vault is the only thing that can undo the work: it stays with the agency, and
        this server will not read it back out.
        """)]
    public static RedactionReport RedactTranscript(
        [Description("Path to the transcript to de-identify. This server reads it; you should not.")]
        string transcriptPath,
        [Description("An opaque id for this record, e.g. \"r-042\". It goes in the manifest, which travels with the corpus, so it must not be the patient's name or the file's.")]
        string recordId,
        [Description("Path to the roster JSON: [{ \"value\": \"Eleanor Vasquez\", \"kind\": \"PatientName\", \"subjectId\": \"patient-1\" }]. Kinds: PatientName, FamilyName, StaffName, OtherName, Phone, Email, Url, IpAddress, Address, PostalCode, Date, AgeOver89, RecordNumber, AccountNumber, DeviceId.")]
        string? rosterPath = null,
        [Description("Path to the vault, created if absent. Pass the same one for every transcript in a corpus so one person keeps one invented name.")]
        string? vaultPath = null,
        [Description("Where to write the redacted text. If given, the text is written there and NOT returned in the response — use this when even the redacted text should stay out of the context.")]
        string? outputPath = null)
    {
        string text = ReadTranscript(transcriptPath);
        var context = BuildContext(recordId, rosterPath);

        PseudonymVault vault = vaultPath is { Length: > 0 }
            ? PseudonymVault.LoadOrCreate(vaultPath)
            : new PseudonymVault();

        var engine = new SiluetaEngine([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()], vault);
        RedactionResult result = engine.Redact(text, context);

        if (vaultPath is { Length: > 0 })
        {
            vault.SaveTo(vaultPath);
        }

        if (outputPath is { Length: > 0 })
        {
            File.WriteAllText(outputPath, result.Text);
        }

        return Report(result, context, vaultPath, outputPath, vault.Count);
    }

    [McpServerTool(Name = "redact_text", ReadOnly = true),
     Description("""
        THE TEXT YOU PASS HERE IS ALREADY EXPOSED. To call this tool you had to read the transcript, so
        it is in your context, in the conversation history, and in whatever the provider logs — and
        nothing this tool returns can undo that. For a transcript of real people, use redact_transcript
        and pass a path instead.

        This exists for text that is already safe to hold: a synthetic example, a corpus you generated,
        a redacted file you are checking. It takes the roster inline as "Name|Kind|SubjectId" entries,
        one per array element, and returns the redacted text and manifest.
        """)]
    public static RedactionReport RedactText(
        [Description("The text to de-identify. Only pass text that is already safe to have in context.")]
        string text,
        [Description("An opaque id for this record. Not a name, not a file name.")]
        string recordId,
        [Description("Roster entries as \"Value|Kind|SubjectId\", e.g. \"Eleanor Vasquez|PatientName|patient-1\". Empty = pattern rules only, and every name survives.")]
        string[]? roster = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var context = new DeidentificationContext(RequireOpaque(recordId, nameof(recordId)));
        foreach (string entry in roster ?? [])
        {
            string[] parts = entry.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || parts[0].Length == 0 || parts[2].Length == 0)
            {
                throw new ArgumentException(
                    $"Roster entry {Quantity(roster)} is not \"Value|Kind|SubjectId\". Every person needs an " +
                    "opaque subject id of your own; it is the key the vault is filed under and it must not be " +
                    "the name.");
            }

            context.AddPerson(parts[2], parts[0], ParseKind(parts[1]));
        }

        var engine = new SiluetaEngine([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()]);
        RedactionResult result = engine.Redact(text, context);

        return Report(result, context, vaultPath: null, outputPath: null, engine.Vault.Count);
    }

    private static string ReadTranscript(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            // Deliberately does not echo the path's contents or guess at a nearby file: a wrong path is
            // a caller error, not an invitation to go looking through the filesystem.
            throw new FileNotFoundException($"No transcript at '{path}'.");
        }

        return File.ReadAllText(path);
    }

    private static DeidentificationContext BuildContext(string recordId, string? rosterPath)
    {
        var context = new DeidentificationContext(RequireOpaque(recordId, nameof(recordId)));
        if (string.IsNullOrWhiteSpace(rosterPath))
        {
            return context;
        }

        if (!File.Exists(rosterPath))
        {
            throw new FileNotFoundException($"No roster at '{rosterPath}'.");
        }

        KnownIdentifierDto[] roster =
            JsonSerializer.Deserialize(File.ReadAllText(rosterPath), SiluetaJsonContext.Default.KnownIdentifierDtoArray) ?? [];

        for (int i = 0; i < roster.Length; i++)
        {
            KnownIdentifierDto entry = roster[i];
            if (string.IsNullOrWhiteSpace(entry.SubjectId))
            {
                // The message names the position, never the value: an error message is one of the ways
                // identified text escapes a process that was supposed to be removing it.
                throw new ArgumentException(
                    $"Roster entry {i + 1} has no \"subjectId\". Give every person an opaque id of your own " +
                    "(\"patient-1\", \"s-7f3\"): it is the key the vault is filed under, and it must not be the name.");
            }

            context.AddPerson(entry.SubjectId, entry.Value, ParseKind(entry.Kind));
        }

        return context;
    }

    private static string RequireOpaque(string recordId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            throw new ArgumentException(
                "A record needs an opaque id of its own. It goes in the manifest, and the manifest leaves " +
                "with the corpus.", parameterName);
        }

        return recordId.Trim();
    }

    private static IdentifierKind ParseKind(string kind) =>
        Enum.TryParse(kind, ignoreCase: true, out IdentifierKind parsed) ? parsed : IdentifierKind.OtherName;

    private static string Quantity(string[]? roster) => roster is null ? "(none)" : $"of {roster.Length}";

    /// <summary>Builds the response. Nothing here is read out of the original text.</summary>
    private static RedactionReport Report(
        RedactionResult result,
        DeidentificationContext context,
        string? vaultPath,
        string? outputPath,
        int vaultSubjects)
    {
        RedactionManifest manifest = result.Manifest;

        return new RedactionReport(
            RedactedText: outputPath is { Length: > 0 } ? null : result.Text,
            WrittenTo: outputPath,
            RecordId: manifest.RecordId,
            Policy: $"{manifest.Policy}/{manifest.PolicyVersion}",
            PolicyFingerprint: manifest.PolicyFingerprint,
            EngineVersion: manifest.EngineVersion,
            SpansReplaced: result.Applied.Count,
            SubjectsInThisRecord: manifest.Subjects,
            SubjectsInVault: vaultSubjects,
            VaultPath: vaultPath,
            ByKind: manifest.ByKind,
            ByDetector: manifest.ByDetector,
            ByMatch: manifest.ByMatch,
            RosterSize: context.Known.Count,
            Caveat: Caveat(context, result));
    }

    /// <summary>
    /// What the caller has to be told alongside the result, every time. The library's whole argument is
    /// that it measures its own failure rate; until that number exists, saying so is the measurement.
    /// </summary>
    private static string Caveat(DeidentificationContext context, RedactionResult result)
    {
        var notes = new List<string>
        {
            "Silueta has not yet measured its own leak rate, so this output is not verified to be " +
            "de-identified. Names nobody wrote down — nicknames, a relative mentioned only by " +
            "relationship, a doctor named once — are invisible to the roster matcher and survive.",
        };

        if (context.Known.Count == 0)
        {
            notes.Add(
                "No roster was given, so only the pattern rules ran: phone, e-mail, URL, IP, record " +
                "numbers, dates and ages over 89. EVERY NAME IN THIS TRANSCRIPT SURVIVED.");
        }

        if (result.Applied.Count == 0)
        {
            notes.Add("Nothing was replaced. Check that the roster describes the people in this record.");
        }

        notes.Add(
            "No rule emits a postal code or a street address yet, and numbers spoken as words " +
            "(\"five five five, oh one four seven\") are not recognised.");

        return string.Join(" ", notes);
    }
}

/// <summary>
/// What a redaction returns. Note what is not here: the values that were removed, the roster, and the
/// vault's subject-to-name mapping. Offsets and counts describe the work; the values would undo it.
/// </summary>
public sealed record RedactionReport(
    string? RedactedText,
    string? WrittenTo,
    string RecordId,
    string Policy,
    string PolicyFingerprint,
    string EngineVersion,
    int SpansReplaced,
    int SubjectsInThisRecord,
    int SubjectsInVault,
    string? VaultPath,
    IReadOnlyDictionary<string, int> ByKind,
    IReadOnlyDictionary<string, int> ByDetector,
    IReadOnlyDictionary<string, int> ByMatch,
    int RosterSize,
    string Caveat);
