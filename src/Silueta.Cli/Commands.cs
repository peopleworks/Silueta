using System.Text.Json;
using Silueta.Core;

namespace Silueta.Cli;

/// <summary>
/// What the commands actually do, with the two output streams passed in rather than reached for.
/// <para>
/// They lived inside the top-level statements, which compile into local functions of a synthesised
/// entry point: unreachable from a test, so the order in which this command writes a file, prints a
/// transcript and checks its own residue was covered by nothing. The MCP server refuses to write when
/// the run did not hold; the CLI wrote first and checked afterwards. Same rule, two copies, and only
/// one of them was true — which is the shape every defect in this project has had.
/// </para>
/// </summary>
public static class Commands
{
    public static int Redact(IReadOnlyDictionary<string, string> options, TextWriter output, TextWriter error)
    {
        if (!options.TryGetValue("in", out string? inputPath) || !File.Exists(inputPath))
        {
            error.WriteLine("silueta redact needs --in <transcript.txt>.");
            return 2;
        }

        // No fall-back to the file name. It used to do that, and "Ana-Perez.txt" then wrote
        // recordId=Ana-Perez into the manifest — the file that travels with the corpus to prove it holds
        // no identifiers. An opaque id is three seconds of the caller's time and it has to be theirs.
        if (!options.TryGetValue("record", out string? recordId) || string.IsNullOrWhiteSpace(recordId))
        {
            error.WriteLine(
                "silueta redact needs --record <opaque-id>: an id of your own, not the file name and not the\n" +
                "patient's. It goes in the manifest, and the manifest leaves with the corpus.");
            return 2;
        }

        string text = File.ReadAllText(inputPath);
        var context = new DeidentificationContext(recordId);

        if (options.TryGetValue("context", out string? contextPath))
        {
            if (!File.Exists(contextPath))
            {
                error.WriteLine($"Roster file not found: {contextPath}");
                return 2;
            }

            KnownIdentifierDto[] known =
                JsonSerializer.Deserialize(File.ReadAllText(contextPath), SiluetaJsonContext.Default.KnownIdentifierDtoArray) ?? [];

            for (int i = 0; i < known.Length; i++)
            {
                KnownIdentifierDto dto = known[i];

                // No falling back to the name. It used to, and that made the subject id — the key the vault
                // is filed under — a copy of the very value being hidden. Anyone holding the vault then held
                // the roster, and the invented name became a function of the real one.
                if (string.IsNullOrWhiteSpace(dto.SubjectId))
                {
                    error.WriteLine(
                        $"Roster entry {i + 1} has no \"subjectId\". Give every person an opaque id of your\n" +
                        "own (\"patient-1\", \"s-7f3\"): it is the key the vault is filed under, and it must not\n" +
                        "be the name.");
                    return 2;
                }

                if (!IdentifierKindExtensions.TryParseName(dto.Kind, out IdentifierKind kind))
                {
                    error.WriteLine(IdentifierKindExtensions.UnreadableKindMessage(i + 1, dto.Kind));
                    return 2;
                }

                context.AddPerson(dto.SubjectId, dto.Value, kind);
            }
        }
        else
        {
            error.WriteLine("warning: no --context roster given; only pattern rules will fire.");
        }

        // The lineage is read before the vault, because it says what the vault mints from.
        SiluetaLineage lineage = SiluetaLineage.Default;
        if (options.TryGetValue("lineage", out string? lineagePath))
        {
            if (!File.Exists(lineagePath))
            {
                error.WriteLine($"Lineage file not found: {lineagePath}");
                return 2;
            }

            try
            {
                lineage = SiluetaLineage.Load(lineagePath);
            }
            catch (InvalidOperationException ex)
            {
                // The message names the rule that was broken and never the transcript, so it can be
                // printed: "pools.given lists 'Álex' twice, ignoring accents and case."
                error.WriteLine($"That lineage cannot be used: {ex.Message}");
                return 2;
            }

            output.WriteLine(
                $"Lineage {lineage.Name}/{lineage.Version} ({lineage.Language}), fingerprint {lineage.Fingerprint}.");
            foreach (string skipped in lineage.Skipped)
            {
                error.WriteLine($"warning: the lineage names '{skipped}', which this build has no kind for.");
            }
        }

        // The vault is read before the run and written after it. It used to be created empty every time and
        // then overwrite --vault, so the second transcript of a corpus silently discarded the first one's
        // assignments — the same person became two people, and neither could be traced back.
        options.TryGetValue("vault", out string? vaultPath);
        PseudonymVault vault = vaultPath is null
            ? new PseudonymVault(lineage.Pools)
            : PseudonymVault.LoadOrCreate(vaultPath, lineage.Pools);

        var engine = SiluetaEngine.FromLineage(lineage, vault);
        RedactionResult result = engine.Redact(text, context);

        // The vault is written FIRST, before any redacted artefact exists. It is the only thing that can
        // undo the work and the only thing with no second copy: a run that wrote the redacted transcript and
        // then failed to write the vault would leave a corpus nobody — not even the agency — can trace back.
        if (vaultPath is not null)
        {
            engine.Vault.SaveTo(vaultPath);
            output.WriteLine(
                $"Wrote {vaultPath} — {engine.Vault.Count} subjects. Keep it inside the agency: it is the only way back.");
        }
        else if (result.Applied.Any(d => d.SubjectId is { Length: > 0 }))
        {
            error.WriteLine(
                "warning: names were replaced but no --vault was given, so the invented names were minted and\n" +
                "thrown away. Nothing in this output can be traced back, and the next run will invent others.");
        }

        // The manifest comes second, and it is written whether the run held or not: it records the
        // residue, so a run that refuses to emit still leaves an account of why.
        if (options.TryGetValue("manifest", out string? manifestPath))
        {
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(result.Manifest, SiluetaJsonContext.Default.RedactionManifest));
            output.WriteLine($"Wrote {manifestPath}.");
        }

        if (result.Residue.Count > 0)
        {
            // The engine read its own output back and still found identifiers in it. Nothing is written
            // and nothing is printed: this used to write --out first and complain afterwards, which left
            // a file named like a redacted transcript that still said somebody's name — and stdout is a
            // pipe, a log, or a model's context. Exit code 3, and no artefact to mistake for a clean one.
            error.WriteLine(
                $"ERROR: this pipeline still finds {result.Residue.Count} identifier(s) in its own output. " +
                "Nothing was written: do not treat this transcript as de-identified.");
            foreach (Detection residual in result.Residue)
            {
                error.WriteLine($"  {residual.Kind} at [{residual.Start},{residual.End}) via {residual.DetectorId}");
            }

            return 3;
        }

        if (options.TryGetValue("out", out string? outPath))
        {
            File.WriteAllText(outPath, result.Text);
            output.WriteLine($"Wrote {outPath} — {result.Applied.Count} spans replaced.");
        }
        else
        {
            output.WriteLine(result.Text);
        }

        return 0;
    }

    // The example is the argument: every name below is damaged the way a speech recogniser damages names.
    public static int Demo(TextWriter output)
    {
        const string transcript = """
            Shift report. Sophia Rays was with Mrs. Ellenor Vasques this morning.
            Her daughter Jamileth called at 602-555-0147 about the 3/14/2026 appointment,
            and Ellie said she would email jamileth.v@example.com. The patient is 94 years old.
            Blood pressure 138 over 82, pain 4 out of 10, and she took the warfarin with breakfast.
            """;

        var context = new DeidentificationContext("demo-001")
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)
            .AddPerson("family-1", "Yamilet Vasquez", IdentifierKind.FamilyName)
            .AddPerson("staff-1", "Sofía Reyes", IdentifierKind.StaffName);

        // Invented names are minted at random and remembered in the vault, so a real run cannot print the
        // same thing twice — which is correct, and no use at all for a README a reader checks against. This
        // vault comes pre-assigned, which is also what a second run in an agency looks like: the names were
        // decided once and the file is what keeps them.
        var vault = new PseudonymVault()
            .Assign("patient-1", "Ale Espinal")
            .Assign("family-1", "Chris Herrera")
            .Assign("staff-1", "Yael Bravo");

        var engine = new SiluetaEngine([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()], vault);
        RedactionResult result = engine.Redact(transcript, context);

        output.WriteLine("--- transcript as the recogniser wrote it ---");
        output.WriteLine(transcript);
        output.WriteLine();
        output.WriteLine("--- de-identified ---");
        output.WriteLine(result.Text);
        output.WriteLine();
        output.WriteLine("--- what was replaced ---");
        foreach (Detection detection in result.Applied)
        {
            // The detection carries offsets, not text. Reading the value back out of the transcript is
            // legitimate here and only here: this is the demo's own input, invented for the README.
            output.WriteLine(
                $"  {detection.Kind,-12} {detection.Match,-8} {detection.Confidence:0.00}  \"{detection.TextIn(transcript)}\"");
        }

        return 0;
    }

    public static int Unknown(string command, TextWriter output, TextWriter error)
    {
        error.WriteLine($"Unknown command '{command}'.");
        PrintUsage(output);
        return 2;
    }

    public static Dictionary<string, string> ParseOptions(ReadOnlySpan<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key = args[i][2..];
            string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";

            options[key] = value;
        }

        return options;
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""
            silueta — de-identification for conversation transcripts.

              silueta demo
                  Runs a built-in example: a transcript with the spelling damage a speech
                  recogniser leaves behind, and what Silueta does with it.

              silueta redact --in <transcript.txt> [--context <roster.json>]
                             [--out <file>] [--manifest <file>] [--vault <file>]
                             [--lineage <file>] [--record <id>]

                  --context  JSON array of { "value", "kind", "subjectId" }, the people this
                             record is about. Without it, only pattern rules fire.
                  --manifest What was removed, under which policy: keep it with the corpus.
                  --vault    Subject id to pseudonym. Never leaves the agency.
                  --lineage  Your own word lists, labels and pattern rules, in your own
                             language. Without it, the lists this library ships with. Its
                             fingerprint goes in the manifest, so a corpus says which
                             lineage produced it.

            Kinds: PatientName, FamilyName, StaffName, OtherName, Phone, Email, Url, IpAddress,
                   Address, PostalCode, Date, AgeOver89, RecordNumber, AccountNumber, DeviceId,
                   Organization, Product, ClientName (a person: the customer, not the company).
            """);
    }
}
