using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Silueta.Core;

int iterations = 7;
if (args.Length > 0 && (args.Length != 2 || args[0] != "--iterations"
    || !int.TryParse(args[1], out iterations) || iterations < 1 || iterations > 100))
{
    Console.Error.WriteLine("Usage: dotnet run -c Release --project tools/Silueta.Performance -- [--iterations 1..100]");
    return 1;
}

var context = new DeidentificationContext("perf-1");
string[] names = ["Carmen Alvarez", "Marisol Benitez", "Jimena Castillo", "Javier Delgado",
    "Luciana Espinosa", "Rodrigo Figueroa", "Eleonora Gutierrez", "Adriana Herrera",
    "Beatriz Maldonado", "Isabel Navarro"];
for (int i = 0; i < names.Length; i++)
{
    context.AddPerson($"subject-{i}", names[i], IdentifierKind.PatientName);
}

const int wordCount = 40_000;
string repeated = RepeatWords("patient resting comfortably breathing normally after breakfast today", wordCount);
string mixed = RepeatWords("Carmin Alvarez spoke with Javier Delgado while Marisol Benites rested comfortably", wordCount);
// Distinct spellings expose the memory cost of a cache when there is little vocabulary reuse.
string distinct = string.Join(' ', Enumerable.Range(0, wordCount).Select(i => $"term{Letters(i)}"));
var detector = new KnownValueDetector();
SiluetaEngine engine = SiluetaEngine.CreateDefault();
SiluetaEngine pinnedEngine = SiluetaEngine.CreateDefault();
pinnedEngine.Vault.Assign("subject-0", "Ale Bravo").Assign("subject-3", "Sol Castro")
    .Assign("subject-1", "Paz Duarte");
string minting = RepeatWords("Carmin Alvarez rested while Carmen spoke today comfortably", wordCount);
var mintingPools = new SurrogatePools(["Ale"], ["Bravo"]);
var crossedContext = new DeidentificationContext("perf-crossed")
    .AddPerson("crossed-1", "Ana Maria", IdentifierKind.PatientName)
    .AddPerson("crossed-2", "Maria Perez", IdentifierKind.PatientName);
string crossed = RepeatWords("Ana Maria Perez rested comfortably today", wordCount);
SiluetaEngine crossedEngine = SiluetaEngine.CreateDefault();

var measurements = new List<object>();
Measure("detector/repeated", repeated, () => new([.. detector.Detect(repeated, context)], null));
Measure("detector/distinct", distinct, () => new([.. detector.Detect(distinct, context)], null));
Measure("detector/matches", mixed, () => new([.. detector.Detect(mixed, context)], null));
Measure("engine/repeated", repeated, () =>
{
    RedactionResult result = engine.Redact(repeated, context);
    return RunOutput.FromResult(result);
});
Measure("engine/matches-pinned", mixed, () => RunOutput.FromResult(pinnedEngine.Redact(mixed, context)));
// A single safe pool combination makes minting reproducible without changing production randomness.
// Engine/vault construction is included: each measured call starts with no assigned subjects.
Measure("engine/matches-mint", minting, () =>
{
    SiluetaEngine freshEngine = SiluetaEngine.FromLineage(SiluetaLineage.Default,
        new PseudonymVault(mintingPools));
    return RunOutput.FromResult(freshEngine.Redact(minting, context));
});
Measure("engine/matches-crossed", crossed,
    () => RunOutput.FromResult(crossedEngine.Redact(crossed, crossedContext)), crossedContext);

Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processors = Environment.ProcessorCount,
    configuration =
#if DEBUG
        "Debug",
#else
        "Release",
#endif
    iterations,
    warmups = 2,
    measurements
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;

void Measure(string name, string text, Func<RunOutput> run, DeidentificationContext? roster = null)
{
    roster ??= context;
    int knownCandidates = detector.Detect(text, roster).Count();
    string? expectedAudit = null;
    for (int i = 0; i < 2; i++)
    {
        Check(run());
    }

    var times = new double[iterations];
    var allocations = new long[iterations];
    RunOutput? output = null;
    for (int i = 0; i < iterations; i++)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        output = run();
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        // Check every measured call outside the timing/allocation window. A random output or a
        // residue makes the workload unsuitable for comparing implementations.
        Check(output);
    }

    Array.Sort(times);
    Array.Sort(allocations);
    measurements.Add(new
    {
        name,
        words = wordCount,
        rosterValues = roster.Known.Count,
        characters = text.Length,
        medianMs = Median(times),
        medianAllocatedBytes = Median(allocations.Select(value => (double)value).ToArray()),
        detections = output!.Detections.Count,
        knownCandidates,
        residualSpans = output.Residue?.Count,
        ambiguousAttributions = output.Manifest?.AmbiguousAttributions,
        // Order, offsets, kind, subject, confidence and match classification all participate.
        detectionSha256 = Digest(JsonSerializer.Serialize(output.Detections)),
        outputSha256 = output.Text is null ? null : Digest(output.Text),
        auditSha256 = expectedAudit
    });

    void Check(RunOutput result)
    {
        string audit = Audit(result);
        expectedAudit ??= audit;
        if (audit != expectedAudit || result.Residue is { Count: > 0 })
        {
            throw new InvalidOperationException($"Case '{name}' produced unstable output or residue.");
        }
    }
}

static double Median(double[] sorted) => sorted.Length % 2 == 1
    ? sorted[sorted.Length / 2]
    : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

static string Audit(RunOutput output)
{
    JsonNode? manifest = JsonSerializer.SerializeToNode(output.Manifest);
    // The wall clock is the only intentionally variable manifest field in these fixtures.
    manifest?.AsObject().Remove(nameof(RedactionManifest.RunUtc));
    return Digest(JsonSerializer.Serialize(new { output.Detections, output.Text, output.Residue, manifest }));
}

static string RepeatWords(string phrase, int count)
{
    string[] words = phrase.Split(' ');
    return string.Join(' ', Enumerable.Range(0, count).Select(i => words[i % words.Length]));
}

static string Letters(int value)
{
    Span<char> letters = stackalloc char[5];
    for (int i = 0; i < letters.Length; i++)
    {
        letters[i] = (char)('a' + value % 26);
        value /= 26;
    }
    return new string(letters);
}

sealed record RunOutput(IReadOnlyList<Detection> Detections, string? Text,
    IReadOnlyList<Detection>? Residue = null, RedactionManifest? Manifest = null)
{
    public static RunOutput FromResult(RedactionResult result) =>
        new(result.Applied, result.Text, result.Residue, result.Manifest);
}
