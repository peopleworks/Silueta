using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

var measurements = new List<object>();
Measure("detector/repeated", repeated, () => new([.. detector.Detect(repeated, context)], null));
Measure("detector/distinct", distinct, () => new([.. detector.Detect(distinct, context)], null));
Measure("detector/matches", mixed, () => new([.. detector.Detect(mixed, context)], null));
Measure("engine/repeated", repeated, () =>
{
    RedactionResult result = engine.Redact(repeated, context);
    return new(result.Applied, result.Text);
});

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
    rosterValues = context.Known.Count,
    measurements
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;

void Measure(string name, string text, Func<RunOutput> run)
{
    for (int i = 0; i < 2; i++)
    {
        run();
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
    }

    Array.Sort(times);
    Array.Sort(allocations);
    measurements.Add(new
    {
        name,
        words = wordCount,
        characters = text.Length,
        medianMs = Median(times),
        medianAllocatedBytes = Median(allocations.Select(value => (double)value).ToArray()),
        detections = output!.Detections.Count,
        // Order, offsets, kind, subject, confidence and match classification all participate.
        detectionSha256 = Digest(JsonSerializer.Serialize(output.Detections)),
        outputSha256 = output.Text is null ? null : Digest(output.Text)
    });
}

static double Median(double[] sorted) => sorted.Length % 2 == 1
    ? sorted[sorted.Length / 2]
    : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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

sealed record RunOutput(IReadOnlyList<Detection> Detections, string? Text);
