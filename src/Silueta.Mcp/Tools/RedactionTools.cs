using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
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
        dates, ages over 89), plus the rule for a person named right after a relationship ("my daughter
        Linda"), and every other name survives.

        Pass vaultPath on every run of one corpus, or each transcript invents different names for the
        same people. The vault is the only thing that can undo the work: it stays with the agency, and
        this server will not read it back out.
        """)]
    public static RedactionReport RedactTranscript(
        [Description("Path to the transcript to de-identify. This server reads it; you should not.")]
        string transcriptPath,
        [Description("An opaque id for this record, e.g. \"r-042\". It goes in the manifest, which travels with the corpus, so it must not be the patient's name or the file's.")]
        string recordId,
        [Description("Path to the roster JSON: [{ \"value\": \"Eleanor Vasquez\", \"kind\": \"PatientName\", \"subjectId\": \"patient-1\" }]. Kinds: PatientName, FamilyName, StaffName, OtherName, ClientName (a person: the customer), Organization (a company, including a client company), Product, Phone, Email, Url, IpAddress, Address, PostalCode, Date, AgeOver89, RecordNumber, AccountNumber, DeviceId, City, State.")]
        string? rosterPath = null,
        [Description("Path to the vault, created if absent. Pass the same one for every transcript in a corpus so one person keeps one invented name.")]
        string? vaultPath = null,
        [Description("Where to write the redacted text. If given, the text is written there and NOT returned in the response — use this when even the redacted text should stay out of the context.")]
        string? outputPath = null)
    {
        string text = ReadTranscript(transcriptPath);
        var context = BuildContext(recordId, rosterPath);

        string? resolvedVault = vaultPath is { Length: > 0 } ? Resolve(vaultPath, nameof(vaultPath)) : null;
        string? resolvedOutput = outputPath is { Length: > 0 } ? Resolve(outputPath, nameof(outputPath)) : null;

        // A run that wrote the transcript over its own vault would destroy the only thing that can undo
        // the work, and it is one typo away when the model writes both paths.
        if (resolvedVault is not null &&
            (string.Equals(resolvedVault, Resolve(transcriptPath, nameof(transcriptPath)), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(resolvedVault, resolvedOutput, StringComparison.OrdinalIgnoreCase)))
        {
            throw new McpException(
                "The vault cannot also be the transcript or the output. It is the only artefact that can " +
                "undo this work and there is no second copy of it.");
        }

        SiluetaLineage lineage = Lineage(Resolve(transcriptPath, nameof(transcriptPath)), resolvedVault, resolvedOutput);

        PseudonymVault vault = resolvedVault is not null
            ? PseudonymVault.LoadOrCreate(resolvedVault, lineage.Pools)
            : new PseudonymVault(lineage.Pools);

        SiluetaEngine engine = SiluetaEngine.FromLineage(lineage, vault);
        RedactionResult result = Run(engine, text, context);

        // The vault first, before any redacted artefact exists: it has no second copy, and a run that
        // wrote the transcript and then failed to write the vault leaves a corpus nobody can trace back.
        if (resolvedVault is not null)
        {
            vault.SaveTo(resolvedVault);
        }

        // Nothing is written when the run did not hold. On disk with a warning beside it is a file
        // someone will eventually treat as de-identified.
        if (resolvedOutput is not null && result.Residue.Count == 0)
        {
            File.WriteAllText(resolvedOutput, result.Text);
        }

        return Report(result, context, vaultPath, resolvedOutput is null || result.Residue.Count > 0 ? null : outputPath, vault.Count, echoesTheFile: true);
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
        [Description("Roster entries as \"Value|Kind|SubjectId\", e.g. \"Eleanor Vasquez|PatientName|patient-1\". Empty = pattern rules only, plus people named right after a relationship (\"my daughter Linda\"); every other name survives.")]
        string[]? roster = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var context = new DeidentificationContext(RequireOpaque(recordId, nameof(recordId)));
        string[] entries = roster ?? [];
        for (int i = 0; i < entries.Length; i++)
        {
            string[] parts = entries[i].Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || parts[0].Length == 0 || parts[2].Length == 0)
            {
                // The position, never the value: an error message is one of the ways identified text
                // escapes a process that was supposed to be removing it. But naming neither leaves the
                // caller nothing to act on, which is what this used to do — it reported the roster's
                // size into the slot that wanted the entry's index.
                throw new McpException(
                    $"Roster entry {i + 1} of {entries.Length} is not \"Value|Kind|SubjectId\". Every person " +
                    "needs an opaque subject id of your own; it is the key the vault is filed under and it " +
                    "must not be the name.");
            }

            context.AddPerson(parts[2], parts[0], ParseKind(parts[1], i + 1));
        }

        SiluetaEngine engine = SiluetaEngine.FromLineage(Lineage());
        RedactionResult result = Run(engine, text, context);

        return Report(result, context, vaultPath: null, outputPath: null, engine.Vault.Count);
    }

    /// <summary>
    /// The environment variable that confines every path this server will touch. Defaults to the
    /// directory the server was started in.
    /// </summary>
    public const string RootVariable = "SILUETA_ROOT";

    /// <summary>
    /// Where this server's lineage lives: the word lists, the labels and the pattern rules an
    /// organisation brought of its own.
    /// <para>
    /// An environment variable and not a tool parameter, deliberately. Which dictionaries a corpus is
    /// redacted with is a decision by whoever set this server up, and the manifest of every transcript
    /// carries its fingerprint; a parameter would put that decision in a string the model writes, and a
    /// model that can choose the word lists can choose a lineage whose "labels" leave everything in
    /// place.
    /// </para>
    /// </summary>
    public const string LineageVariable = "SILUETA_LINEAGE";

    /// <summary>
    /// Which of the lineage's policies this server redacts under; Safe Harbor when unset. An environment variable
    /// for the same reason as <see cref="LineageVariable"/>, and a sharper one: a model that could pick the policy
    /// could pick the one that keeps everything, and the manifest would name it honestly while the text went out
    /// identified. A name the lineage does not define stops the run rather than falling back — the operator asked
    /// for a policy by name and would otherwise describe the corpus wrongly.
    /// </summary>
    public const string PolicyVariable = "SILUETA_POLICY";

    private static string Root =>
        Path.GetFullPath(Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } configured
            ? configured
            : Directory.GetCurrentDirectory());

    /// <summary>
    /// Resolves a path the model wrote, and refuses anything outside the configured root.
    /// <para>
    /// Model-written paths are the whole attack surface of this server. Without this, the tool below is
    /// a general-purpose file reader wearing a reassuring name.
    /// </para>
    /// </summary>
    private static string Resolve(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException($"{parameterName} is required.");
        }

        string full = Path.GetFullPath(path);

        // The trailing separator matters: without it, a sibling directory whose name merely starts with
        // the root's name ("C:\corpus-old" against a root of "C:\corpus") would pass a prefix test.
        string root = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;

        // Case folding is the file system's rule, not ours. On Linux — where the CI runs, and where
        // anyone cloning this will run it — "/data/Corpus/x" and "/data/corpus/" are two directories,
        // so an ignore-case prefix test lets a path out of the root it was supposed to be confined to.
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!full.StartsWith(root, comparison))
        {
            throw new McpException(
                $"'{parameterName}' is outside the directory this server is allowed to touch. Set " +
                $"{RootVariable} to the folder holding the corpus, or start the server there.");
        }

        return full;
    }

    /// <summary>
    /// The lineage this server runs with, or the one built into the library when none is configured.
    /// Read on every call rather than cached: an operator who fixes a word list wants the next transcript
    /// to use it, not the next restart.
    /// </summary>
    private static SiluetaLineage Lineage(string? mustDifferFrom = null, string? andFrom = null, string? andAlsoFrom = null)
    {
        string? configured = Environment.GetEnvironmentVariable(LineageVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return SiluetaLineage.Default;
        }

        string full = Resolve(configured, LineageVariable);

        if (!File.Exists(full))
        {
            throw new McpException($"{LineageVariable} points at a file that is not there.");
        }

        foreach (string? other in (string?[])[mustDifferFrom, andFrom, andAlsoFrom])
        {
            if (other is not null && string.Equals(full, other, StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException(
                    $"{LineageVariable} is the same file as the transcript, the vault or the output of " +
                    "this run. A lineage is configuration, and it is about to be overwritten or read as " +
                    "something it is not.");
            }
        }

        string text = File.ReadAllText(full);

        // Same rule as the transcript reader, for the same reason: a vault is the one artefact that can
        // undo a redaction, and this path is the fourth way into the filesystem this server has.
        if (LooksLikeAVault(text))
        {
            throw new McpException($"{LineageVariable} points at a vault. This server will not read one.");
        }

        try
        {
            return SiluetaLineage.FromJson(text);
        }
        catch (InvalidOperationException ex)
        {
            // The loader's messages name the rule that was broken and never any transcript content.
            throw new McpException($"{LineageVariable} cannot be used: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the engine and lets its own refusals reach the caller.
    /// <para>
    /// The SDK replaces an ordinary exception's message with a generic one before it reaches the client,
    /// which is the right default for a server that handles transcripts. But the engine's argument
    /// failures are the caller's own mistakes — a record id that names the patient, a roster entry with
    /// no subject id — and they are written to carry no value from the text, by the same rule that keeps
    /// values off <see cref="Detection"/>. Unheard, they leave the model guessing at a tool that simply
    /// stopped working.
    /// </para>
    /// </summary>
    private static RedactionResult Run(SiluetaEngine engine, string text, DeidentificationContext context)
    {
        SiluetaPolicy policy;
        try
        {
            policy = engine.Lineage.Policy(
                Environment.GetEnvironmentVariable(PolicyVariable) is { Length: > 0 } named ? named : SiluetaPolicy.SafeHarbor.Name);
        }
        catch (InvalidOperationException ex)
        {
            throw new McpException($"{PolicyVariable}: {ex.Message}");
        }

        try
        {
            return engine.Redact(text, context, policy);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    private static string ReadTranscript(string path)
    {
        string full = Resolve(path, nameof(path));

        if (!File.Exists(full))
        {
            // Deliberately does not echo the path's contents or guess at a nearby file: a wrong path is
            // a caller error, not an invitation to go looking through the filesystem.
            throw new McpException($"No transcript at '{path}'.");
        }

        string text = File.ReadAllText(full);

        // The vault maps every subject to the invented name that stands for them in the corpus. Fed to
        // the tool below with no roster, nothing matched and the whole table came back to the model,
        // reported as safe to export. Three documents promise this server will never re-identify anyone;
        // that promise was kept by not DECLARING such a tool, while the capability sat in this read.
        if (LooksLikeAVault(text))
        {
            throw new McpException(
                "That file is a vault, and a vault is the one artefact that can undo a redaction. This " +
                "server will not read one back out — not as a transcript, not for any reason.");
        }

        return text;
    }

    /// <summary>Cheap and deliberately over-eager: a false positive costs a renamed file.</summary>
    private static bool LooksLikeAVault(string text)
    {
        if (text.Contains("\"pseudonym\"", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("SIL-", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return JsonSerializer.Deserialize(text, SiluetaJsonContext.Default.VaultFile) is { Subjects.Count: > 0 };
        }
        catch (JsonException)
        {
            return false;
        }
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
            throw new McpException($"No roster at '{rosterPath}'.");
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
                throw new McpException(
                    $"Roster entry {i + 1} has no \"subjectId\". Give every person an opaque id of your own " +
                    "(\"patient-1\", \"s-7f3\"): it is the key the vault is filed under, and it must not be the name.");
            }

            context.AddPerson(entry.SubjectId, entry.Value, ParseKind(entry.Kind, i + 1));
        }

        return context;
    }

    private static string RequireOpaque(string recordId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            // McpException rather than ArgumentException: the SDK replaces an ordinary exception's
            // message with a generic one before it reaches the client, which is the right default for a
            // tool that handles transcripts — but these particular messages are written to carry no
            // value from the text, only the caller's own mistake, and they are useless unheard.
            _ = parameterName;
            throw new McpException(
                "A record needs an opaque id of its own. It goes in the manifest, and the manifest leaves " +
                "with the corpus.");
        }

        return recordId.Trim();
    }

    /// <summary>A roster kind, or a refusal. It used to fall back to OtherName, which after the company
    /// kinds sends a misspelled company to the pool of people's names without a word.</summary>
    private static IdentifierKind ParseKind(string? kind, int entryNumber) =>
        IdentifierKindExtensions.TryParseName(kind, out IdentifierKind parsed)
            ? parsed
            : throw new McpException(IdentifierKindExtensions.UnreadableKindMessage(entryNumber, kind));

    /// <summary>Builds the response. Nothing here is read out of the original text.</summary>
    private static RedactionReport Report(
        RedactionResult result,
        DeidentificationContext context,
        string? vaultPath,
        string? outputPath,
        int vaultSubjects,
        bool echoesTheFile = false)
    {
        RedactionManifest manifest = result.Manifest;

        // Whether the redacted text may go back to the model at all. This is the decision the tool
        // exists to make, and it used to be made on one condition (did the caller ask for a file?)
        // while three others mattered just as much.
        string? withheld =
            result.Residue.Count > 0 && echoesTheFile
                ? $"The text is withheld: after redacting, this pipeline still finds {result.Residue.Count} " +
                  "identifier(s) in its own output, so returning it would put them in your context — where " +
                  "nothing can take them back. Tell the user; do not ask for it another way."
            : result.Residue.Count > 0
                // Nothing to protect here: the caller pasted this text, so it is already in the context.
                // Saying otherwise would be theatre, and the reason to withhold stands without it — a
                // text this pipeline still finds names in must not be passed on as de-identified.
                ? $"The text is withheld: after redacting, this pipeline still finds {result.Residue.Count} " +
                  "identifier(s) in its own output. It is not de-identified and must not be saved, quoted " +
                  "or passed on as if it were. Tell the user."
            : echoesTheFile && context.Known.Count == 0
                ? "The text is withheld: no roster was given, so no name could be found and every name in " +
                  "this transcript survived. Returning it would hand you the file you deliberately did " +
                  "not open. Pass rosterPath naming who this record is about, or use outputPath to write " +
                  "the pattern-only result to disk without it entering your context."
            : echoesTheFile && result.Applied.Count == 0
                ? "The text is withheld: nothing was replaced, so the \"redacted\" text would be the file " +
                  "itself. Returning it would make this tool a file reader. Check the roster describes the " +
                  "people in this record."
            : outputPath is { Length: > 0 }
                ? $"The text was written to {outputPath} rather than returned."
                : null;

        return new RedactionReport(
            RedactedText: withheld is null ? result.Text : null,
            Withheld: withheld,
            WrittenTo: outputPath,
            RecordId: manifest.RecordId,
            Policy: $"{manifest.Policy}/{manifest.PolicyVersion}",
            PolicyFingerprint: manifest.PolicyFingerprint,
            Lineage: $"{manifest.Lineage}/{manifest.LineageVersion} ({manifest.LineageLanguage})",
            LineageFingerprint: manifest.LineageFingerprint,
            EngineVersion: manifest.EngineVersion,
            SpansReplaced: result.Applied.Count,
            ResidualSpans: result.Residue.Count,
            SafeToExport: result.Residue.Count == 0,
            SubjectsInThisRecord: manifest.Subjects,
            SubjectsInVault: vaultSubjects,
            VaultPath: vaultPath,
            ByKind: manifest.ByKind,
            ByDetector: manifest.ByDetector,
            ByMatch: manifest.ByMatch,
            RosterSize: context.Known.Count,
            Caveat: Caveats.Paragraph(context, result));
    }

}

/// <summary>
/// What a redaction returns. Note what is not here: the values that were removed, the roster, and the
/// vault's subject-to-name mapping. Offsets and counts describe the work; the values would undo it.
/// </summary>
public sealed record RedactionReport(
    string? RedactedText,

    /// <summary>Why the redacted text is not in this response, or null when it is. Read it out to the
    /// user rather than reaching for another route to the same text.</summary>
    string? Withheld,

    string? WrittenTo,
    string RecordId,
    string Policy,
    string PolicyFingerprint,

    /// <summary>Whose word lists, labels and rules this run used, and a digest of their content. Two
    /// corpora naming the same lineage and version can still have been redacted with different word
    /// lists; the fingerprint is what tells them apart.</summary>
    string Lineage,
    string LineageFingerprint,

    string EngineVersion,
    int SpansReplaced,

    /// <summary>How many identifiers this same pipeline can still find in its own output. Anything other
    /// than zero means the run did not hold and the text must not be treated as de-identified.</summary>
    int ResidualSpans,

    /// <summary>False when ResidualSpans is not zero. Not a guarantee when true: see Caveat.</summary>
    bool SafeToExport,

    int SubjectsInThisRecord,
    int SubjectsInVault,
    string? VaultPath,
    IReadOnlyDictionary<string, int> ByKind,
    IReadOnlyDictionary<string, int> ByDetector,
    IReadOnlyDictionary<string, int> ByMatch,
    int RosterSize,
    string Caveat);
