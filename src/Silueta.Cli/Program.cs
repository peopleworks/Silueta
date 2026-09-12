using System.Text.Json;
using Silueta.Core;

// A hand-rolled argument parser, because Core has no dependencies and the tool that demonstrates it
// should not need three of them to read two flags.
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

string command = args[0];
Dictionary<string, string> options = ParseOptions(args.AsSpan(1));

return command switch
{
    "redact" => Redact(options),
    "demo" => Demo(),
    _ => Unknown(command),
};

static int Redact(Dictionary<string, string> options)
{
    if (!options.TryGetValue("in", out string? inputPath) || !File.Exists(inputPath))
    {
        Console.Error.WriteLine("silueta redact needs --in <transcript.txt>.");
        return 2;
    }

    // No fall-back to the file name. It used to do that, and "Ana-Perez.txt" then wrote
    // recordId=Ana-Perez into the manifest — the file that travels with the corpus to prove it holds
    // no identifiers. An opaque id is three seconds of the caller's time and it has to be theirs.
    if (!options.TryGetValue("record", out string? recordId) || string.IsNullOrWhiteSpace(recordId))
    {
        Console.Error.WriteLine(
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
            Console.Error.WriteLine($"Roster file not found: {contextPath}");
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
                Console.Error.WriteLine(
                    $"Roster entry {i + 1} has no \"subjectId\". Give every person an opaque id of your\n" +
                    "own (\"patient-1\", \"s-7f3\"): it is the key the vault is filed under, and it must not\n" +
                    "be the name.");
                return 2;
            }

            IdentifierKind kind = Enum.TryParse(dto.Kind, ignoreCase: true, out IdentifierKind parsed)
                ? parsed
                : IdentifierKind.OtherName;

            context.AddPerson(dto.SubjectId, dto.Value, kind);
        }
    }
    else
    {
        Console.Error.WriteLine("warning: no --context roster given; only pattern rules will fire.");
    }

    // The vault is read before the run and written after it. It used to be created empty every time and
    // then overwrite --vault, so the second transcript of a corpus silently discarded the first one's
    // assignments — the same person became two people, and neither could be traced back.
    options.TryGetValue("vault", out string? vaultPath);
    PseudonymVault vault = vaultPath is null ? new PseudonymVault() : PseudonymVault.LoadOrCreate(vaultPath);

    var engine = new SiluetaEngine([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()], vault);
    RedactionResult result = engine.Redact(text, context);

    // The vault is written FIRST, before any redacted artefact exists. It is the only thing that can
    // undo the work and the only thing with no second copy: a run that wrote the redacted transcript and
    // then failed to write the vault would leave a corpus nobody — not even the agency — can trace back.
    if (vaultPath is not null)
    {
        engine.Vault.SaveTo(vaultPath);
        Console.WriteLine(
            $"Wrote {vaultPath} — {engine.Vault.Count} subjects. Keep it inside the agency: it is the only way back.");
    }
    else if (result.Applied.Any(d => d.SubjectId is { Length: > 0 }))
    {
        Console.Error.WriteLine(
            "warning: names were replaced but no --vault was given, so the invented names were minted and\n" +
            "thrown away. Nothing in this output can be traced back, and the next run will invent others.");
    }

    if (options.TryGetValue("out", out string? outPath))
    {
        File.WriteAllText(outPath, result.Text);
        Console.WriteLine($"Wrote {outPath} — {result.Applied.Count} spans replaced.");
    }
    else
    {
        Console.WriteLine(result.Text);
    }

    if (options.TryGetValue("manifest", out string? manifestPath))
    {
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(result.Manifest, SiluetaJsonContext.Default.RedactionManifest));
        Console.WriteLine($"Wrote {manifestPath}.");
    }

    if (result.Residue.Count > 0)
    {
        // The engine read its own output back and still found identifiers in it. Exit code 3, not 0:
        // whatever runs next must not treat this file as de-identified.
        Console.Error.WriteLine(
            $"ERROR: this pipeline still finds {result.Residue.Count} identifier(s) in its own output. " +
            "Do not export this transcript.");
        foreach (Detection residual in result.Residue)
        {
            Console.Error.WriteLine($"  {residual.Kind} at [{residual.Start},{residual.End}) via {residual.DetectorId}");
        }

        return 3;
    }

    return 0;
}

// The example is the argument: every name below is damaged the way a speech recogniser damages names.
static int Demo()
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

    Console.WriteLine("--- transcript as the recogniser wrote it ---");
    Console.WriteLine(transcript);
    Console.WriteLine();
    Console.WriteLine("--- de-identified ---");
    Console.WriteLine(result.Text);
    Console.WriteLine();
    Console.WriteLine("--- what was replaced ---");
    foreach (Detection detection in result.Applied)
    {
        // The detection carries offsets, not text. Reading the value back out of the transcript is
        // legitimate here and only here: this is the demo's own input, invented for the README.
        Console.WriteLine(
            $"  {detection.Kind,-12} {detection.Match,-8} {detection.Confidence:0.00}  \"{detection.TextIn(transcript)}\"");
    }

    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 2;
}

static Dictionary<string, string> ParseOptions(ReadOnlySpan<string> args)
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

static void PrintUsage()
{
    Console.WriteLine("""
        silueta — de-identification for conversation transcripts.

          silueta demo
              Runs a built-in example: a transcript with the spelling damage a speech
              recogniser leaves behind, and what Silueta does with it.

          silueta redact --in <transcript.txt> [--context <roster.json>]
                         [--out <file>] [--manifest <file>] [--vault <file>]
                         [--record <id>]

              --context  JSON array of { "value", "kind", "subjectId" }, the people this
                         record is about. Without it, only pattern rules fire.
              --manifest What was removed, under which policy: keep it with the corpus.
              --vault    Subject id to pseudonym. Never leaves the agency.

        Kinds: PatientName, FamilyName, StaffName, OtherName, Phone, Email, Url, IpAddress,
               Address, PostalCode, Date, AgeOver89, RecordNumber, AccountNumber, DeviceId.
        """);
}
