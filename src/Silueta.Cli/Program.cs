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

    string text = File.ReadAllText(inputPath);
    string recordId = options.GetValueOrDefault("record", Path.GetFileNameWithoutExtension(inputPath));
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

        foreach (KnownIdentifierDto dto in known)
        {
            IdentifierKind kind = Enum.TryParse(dto.Kind, ignoreCase: true, out IdentifierKind parsed)
                ? parsed
                : IdentifierKind.OtherName;

            string subject = string.IsNullOrWhiteSpace(dto.SubjectId) ? dto.Value : dto.SubjectId;
            context.AddPerson(subject, dto.Value, kind);
        }
    }
    else
    {
        Console.Error.WriteLine("warning: no --context roster given; only pattern rules will fire.");
    }

    SiluetaEngine engine = SiluetaEngine.CreateDefault();
    RedactionResult result = engine.Redact(text, context);

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

    if (options.TryGetValue("vault", out string? vaultPath))
    {
        File.WriteAllText(vaultPath, engine.Vault.ToJson());
        Console.WriteLine($"Wrote {vaultPath} — keep this inside the agency, it is the only way back.");
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

    RedactionResult result = SiluetaEngine.CreateDefault().Redact(transcript, context);

    Console.WriteLine("--- transcript as the recogniser wrote it ---");
    Console.WriteLine(transcript);
    Console.WriteLine();
    Console.WriteLine("--- de-identified ---");
    Console.WriteLine(result.Text);
    Console.WriteLine();
    Console.WriteLine("--- what was replaced ---");
    foreach (Detection detection in result.Applied)
    {
        Console.WriteLine($"  {detection.Kind,-12} {detection.Match,-8} {detection.Confidence:0.00}  \"{detection.Text}\"");
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
