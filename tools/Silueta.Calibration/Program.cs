using System.Globalization;
using Silueta.Calibration;

// Re-measures this build against the committed gold corpus and publishes the result, in both of the places
// that carry it. Argument parsing and dispatch only: what gets written, and in what order, lives in
// Calibrator, where a test can reach it.
//
//   dotnet run --project tools/Silueta.Calibration
//   dotnet run --project tools/Silueta.Calibration -- --measured-on 2026-09-16
//
// It rewrites files in the working tree and is meant to be run by a maintainer who then reads the diff.

Dictionary<string, string> options = ParseOptions(args);

string root = options.TryGetValue("root", out string? given) ? given : Calibrator.FindRepositoryRoot();

// The day this build last reproduced these numbers, which is what the README's table dates. Local, not UTC:
// it is a publication date on a page, read by whoever ran the tool, not an instant anything is ordered by.
string measuredOn = options.TryGetValue("measured-on", out string? date)
    ? date
    : DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

return Calibrator.Run(root, measuredOn, Console.Out);

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        string key = args[i][2..];
        options[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[++i]
            : "true";
    }

    return options;
}
