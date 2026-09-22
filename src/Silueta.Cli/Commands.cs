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

        // The policy, by name from the lineage, and Safe Harbor when none is asked for. A lineage offering a
        // looser policy does not make it the default: keeping dates is something an operator says out loud.
        SiluetaPolicy policy;
        try
        {
            policy = lineage.Policy(options.TryGetValue("policy", out string? policyName) ? policyName : SiluetaPolicy.SafeHarbor.Name);
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine(ex.Message);
            return 2;
        }

        if (!ReferenceEquals(policy, SiluetaPolicy.SafeHarbor))
        {
            error.WriteLine(
                $"warning: redacting under '{policy.Name}/{policy.Version}', not Safe Harbor. The manifest lists " +
                "every departure; under HIPAA the result is not de-identified without an expert determination.");
        }

        // The vault is read before the run and written after it. It used to be created empty every time and
        // then overwrite --vault, so the second transcript of a corpus silently discarded the first one's
        // assignments — the same person became two people, and neither could be traced back.
        options.TryGetValue("vault", out string? vaultPath);
        PseudonymVault vault = vaultPath is null
            ? new PseudonymVault(lineage.Pools)
            : PseudonymVault.LoadOrCreate(vaultPath, lineage.Pools);

        var engine = SiluetaEngine.FromLineage(lineage, vault);
        RedactionResult result = engine.Redact(text, context, policy);

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

    /// <summary>
    /// Runs the pipeline over a gold corpus under every configuration — Silueta, the literal-roster
    /// baseline, and each detector on its own — and prints the leak rate of each with its interval.
    /// <para>
    /// The console gets numbers and the report file gets numbers, offsets and kinds: never a value from the
    /// corpus, because this command will be pointed at transcripts under an NDA. The corpus caveats are
    /// printed before any rate, so a rate cannot be copied out of the terminal without them above it.
    /// </para>
    /// </summary>
    public static int Evaluate(IReadOnlyDictionary<string, string> options, TextWriter output, TextWriter error)
    {
        if (!options.TryGetValue("gold", out string? goldPath))
        {
            error.WriteLine("silueta evaluate needs --gold <directory of gold documents>.");
            return 2;
        }

        GoldCorpus corpus;
        SiluetaLineage lineage = SiluetaLineage.Default;
        try
        {
            corpus = GoldCorpus.Load(goldPath);
            if (options.TryGetValue("lineage", out string? lineagePath))
            {
                lineage = SiluetaLineage.Load(lineagePath);
            }
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine(ex.Message);
            return 2;
        }

        EvaluationReport report = Evaluation.Run(
            corpus,
            lineage,
            EvaluationConfiguration.Silueta,
            EvaluationConfiguration.DenyList,
            EvaluationConfiguration.KnownValuesOnly,
            EvaluationConfiguration.PatternsOnly);

        output.WriteLine(
            $"Corpus: {report.Corpus.Documents} documents ({string.Join(", ", report.Corpus.Sources.Select(s => $"{s.Key}: {s.Value}"))}). " +
            $"Engine {report.EngineVersion}, lineage {report.Lineage}, policy fingerprint {report.PolicyFingerprint}.");
        foreach (string caveat in report.Corpus.Caveats)
        {
            output.WriteLine($"  caveat: {caveat}");
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        output.WriteLine();
        output.WriteLine("  Every kind the annotators marked — can this corpus leave the building?");
        foreach (ConfigurationResult configuration in report.Configurations)
        {
            output.WriteLine(
                $"    {configuration.Name,-18} leak rate {configuration.LeakRate}  " +
                $"recall {configuration.PooledRecall.ToString("0.000", invariant)}  " +
                $"over-redacted {configuration.OverRedactedCharacters} chars");
        }

        output.WriteLine("    ner                not run: no NER baseline is part of this build.");

        output.WriteLine();
        output.WriteLine("  In scope — kinds this build has a way to find. Judges the matcher.");
        output.WriteLine($"    ({report.Scope})");
        foreach (ConfigurationResult configuration in report.Configurations)
        {
            output.WriteLine(
                $"    {configuration.Name,-18} leak rate {configuration.LeakRateInScope}  " +
                $"recall {configuration.RecallInScope.ToString("0.000", invariant)}");
        }

        foreach (PairedComparison comparison in report.Comparisons)
        {
            output.WriteLine();
            output.WriteLine(
                $"  {comparison.Configuration} minus {comparison.Baseline}, in scope, paired bootstrap over documents " +
                $"({comparison.Resamples} resamples, seed {comparison.Seed}):");
            output.WriteLine($"    recall    {comparison.RecallDifference}");
            output.WriteLine($"    leak rate {comparison.LeakRateDifference}");
        }

        if (options.TryGetValue("out", out string? outPath))
        {
            File.WriteAllText(outPath, JsonSerializer.Serialize(report, SiluetaJsonContext.Default.EvaluationReport));
            output.WriteLine();
            output.WriteLine($"Wrote {outPath}.");
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

        // The demo is the shop window, and a shop window that shows only what worked is an advertisement.
        // This build knows how often it fails and carries the figure, so the demo ends with it — printed
        // from the embedded measurement, never typed here, so it cannot go stale while the page still sells.
        output.WriteLine();
        output.WriteLine("--- and how often this build is wrong ---");
        output.WriteLine(PublishedLeakRate.Current?.Summary ?? RedactionManifest.NoMeasurement);

        return 0;
    }

    /// <summary>
    /// Prints the built-in lineage, which is the file to start your own from: <c>silueta lineage &gt;
    /// mine.json</c>. Writing one from a blank page means rediscovering which keys exist and which pools are
    /// required, and this one is known to load.
    /// </summary>
    public static int Lineage(TextWriter output)
    {
        output.Write(SiluetaLineage.DefaultJson);
        return 0;
    }

    /// <summary>
    /// The word lists a pattern rule can name between double braces, and how many words each holds. What a
    /// rule may say instead of spelling a country into every regular expression — and, for an organisation
    /// writing its own, what is already there before it adds one.
    /// </summary>
    public static int Lists(TextWriter output)
    {
        output.WriteLine("Word lists a pattern rule can name, written as {{name}} inside a regex:");
        output.WriteLine();

        foreach (string name in PatternLists.Names.Order(StringComparer.Ordinal))
        {
            // The count, not the words: the lists are data in the repository, and printing every street
            // suffix of the United States into a terminal helps nobody.
            int words = PatternLists.Expand("{{" + name + "}}").Split('|').Length;
            output.WriteLine(("  {{" + name + "}}").PadRight(26) + words.ToString(System.Globalization.CultureInfo.InvariantCulture) + " words");
        }

        output.WriteLine();
        output.WriteLine("Each comes from a published standard, cited in the file it lives in:");
        output.WriteLine("src/Silueta.Core/Detectors/Packs/lists.core.json. A lineage adds its own under");
        output.WriteLine("\"lists\", and its rules name those the same way \u2014 start from \"silueta lineage\".");

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
                             [--lineage <file>] [--policy <name>] [--record <id>]

                  --context  JSON array of { "value", "kind", "subjectId" }, the people this
                             record is about. Without it, only pattern rules fire.
                  --manifest What was removed, under which policy: keep it with the corpus.
                  --vault    Subject id to pseudonym. Never leaves the agency.
                  --lineage  Your own word lists, labels and pattern rules, in your own
                             language. Without it, the lists this library ships with. Its
                             fingerprint goes in the manifest, so a corpus says which
                             lineage produced it.
                  --policy   A policy the lineage defines, by name. Without it, Safe
                             Harbor. A policy that keeps what Safe Harbor removes is
                             written into the manifest, departure by departure.

              silueta lineage

                  Prints the lineage this build ships with — the word lists, the labels, the
                  generalisations — as the file to start your own from:
                  silueta lineage > mine.json, then edit it and pass it with --lineage.

              silueta lists

                  The word lists a pattern rule can name, as {{us-state}} or {{month-es}},
                  and how many words each holds. A lineage brings its own the same way.

              silueta evaluate --gold <directory> [--out <report.json>] [--lineage <file>]

                  Scores a gold corpus: the leak rate, with its interval, for Silueta,
                  for the same roster matched literally, and for each detector on its
                  own. The report holds offsets and counts, never a value from the corpus.

            Kinds: PatientName, FamilyName, StaffName, OtherName, Phone, Email, Url, IpAddress,
                   Address, PostalCode, Date, AgeOver89, RecordNumber, AccountNumber, DeviceId,
                   Organization, Product, ClientName (a person: the customer, not the company),
                   City, State.
            """);
    }
}
