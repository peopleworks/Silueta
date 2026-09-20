using System.Globalization;
using System.Text.Json;
using Silueta.Core;

namespace Silueta.Calibration;

/// <summary>
/// Measures this build against the committed gold corpus and writes the result into the two places that
/// publish it: the JSON embedded in <c>Silueta.Core</c>, and the table in the README.
/// <para>
/// Both, in one run, on purpose. Every defect this project has found in itself has the same shape — one
/// rule, applied in one place, while a second copy of it lives somewhere else and disagrees. A published
/// number is exactly that kind of rule: it is in the README, in the skill, and now in the assembly. Writing
/// two of the three by hand is arranging for them to drift, and the drift would be in the direction that
/// flatters.
/// </para>
/// <para>
/// It lives in a class and not in the entry point for the reason the CLI's commands do: what a tool writes,
/// and in what order, is a rule, and the compiler turns top-level statements into local functions of a
/// synthetic entry point that no test can reach. What the README's published block says is decided here,
/// so a test renders it and holds the page to it.
/// </para>
/// </summary>
public static class Calibrator
{
    /// <summary>The corpus id the published measurement carries, matching the directory it is loaded from.</summary>
    public const string CorpusId = "silueta-tts-asr";

    /// <summary>The two configurations the README's table compares: what ships, and the literal roster match
    /// that says what matching by sound is worth.</summary>
    public static EvaluationReport Measure(string root) => Evaluation.Run(
        GoldCorpus.Load(Path.Combine(root, "corpus-synthetic", "tts-asr")),
        EvaluationConfiguration.Silueta,
        EvaluationConfiguration.DenyList);

    /// <summary>Writes the embedded measurement, rewrites the README's block, and says what it did.</summary>
    public static int Run(string root, string measuredOn, TextWriter output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(output);

        string jsonPath = Path.Combine(root, "src", "Silueta.Core", "Evaluation", "published-leak-rate.json");
        string readmePath = Path.Combine(root, "README.md");

        EvaluationReport report = Measure(root);
        PublishedLeakRate published = PublishedLeakRate.From(report, CorpusId, measuredOn);

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(published, SiluetaJsonContext.Default.PublishedLeakRate));
        output.WriteLine($"Wrote {Path.GetRelativePath(root, jsonPath)}.");

        string readme = File.ReadAllText(readmePath);
        string rewritten = ReplaceBlock(readme, "leak-rate", RenderTable(report, published));
        if (rewritten == readme)
        {
            output.WriteLine("README.md already said this.");
        }
        else
        {
            File.WriteAllText(readmePath, rewritten);
            output.WriteLine("Rewrote the leak-rate block in README.md.");
        }

        output.WriteLine();
        output.WriteLine(published.Summary);
        output.WriteLine();
        output.WriteLine("Rebuild Silueta.Core for the embedded copy to change, then run the tests: the README, the");
        output.WriteLine("skill and the embedded file are each held to a fresh evaluation, and they fail separately.");

        return 0;
    }

    /// <summary>
    /// The table the README publishes.
    /// <para>
    /// The prose in the first column and the two column headings belong to the README and are written here
    /// only so the whole block can be regenerated. Every number comes from the report, and nothing here
    /// formats a rate or an interval itself: <c>LeakRateEstimate</c> and <c>Interval</c> own how those are
    /// written, so the page and the command line cannot word one measurement two ways.
    /// </para>
    /// </summary>
    public static string RenderTable(EvaluationReport report, PublishedLeakRate published)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(published);

        ConfigurationResult silueta = report.Configurations.Single(c => c.Name == EvaluationConfiguration.Silueta.Name);
        ConfigurationResult literal = report.Configurations.Single(c => c.Name == EvaluationConfiguration.DenyList.Name);
        PairedComparison thesis = report.Comparisons.Single();

        var invariant = CultureInfo.InvariantCulture;
        string when = DateOnly.ParseExact(published.MeasuredOn, "yyyy-MM-dd", invariant).ToString("d MMM yyyy", invariant);

        // The fourth component of an assembly version is noise on a page: 0.1.0.0 is published as 0.1.0.
        string engine = report.EngineVersion[..report.EngineVersion.LastIndexOf('.')];

        // Joined with a bare newline rather than built with AppendLine, whose Environment.NewLine would put
        // CRLF inside a page that is LF everywhere else. ReplaceBlock puts the document's own ending back.
        return string.Join("\n", (string[])[
            $"| {report.Corpus.Documents} documents · {when} · engine {engine} · lineage `{report.Lineage}` " +
            "| Silueta | The same roster, matched literally |",

            "| --- | --- | --- |",

            $"| **Every kind marked** — could this corpus leave the building? | {silueta.LeakRate} | {literal.LeakRate} |",

            "| **In scope** — the kinds this build has a way to find " +
            $"| {silueta.LeakRateInScope} · recall {silueta.RecallInScope.ToString("0.000", invariant)} " +
            $"| {literal.LeakRateInScope} · recall {literal.RecallInScope.ToString("0.000", invariant)} |",

            "| **What matching by sound is worth** — Silueta minus literal, in scope, paired bootstrap over documents " +
            $"| recall {thesis.RecallDifference} · leak rate {thesis.LeakRateDifference} | — |",
        ]);
    }

    /// <summary>
    /// Replaces what sits between two markers, keeping the markers themselves. Anchored to the marker text
    /// and never to a line count: the README's demo guard learned that when it counted code fences from the
    /// top of the file and silently followed whichever block happened to be third.
    /// </summary>
    public static string ReplaceBlock(string document, string name, string contents)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(contents);

        int start = document.IndexOf($"<!-- {name}:start", StringComparison.Ordinal);
        int end = document.IndexOf($"<!-- {name}:end", StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException(
                $"README.md has no {name} block between markers, so there is nowhere to publish the number.");
        }

        int afterStart = document.IndexOf("-->", start, StringComparison.Ordinal);
        if (afterStart < 0 || afterStart > end)
        {
            throw new InvalidOperationException($"The {name}:start marker in README.md is not closed.");
        }

        // Whatever the page is already written in. A generated block in the other convention turns every
        // later diff of this file into noise, and what is being reviewed here is a number.
        string newline = document.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        afterStart += "-->".Length;
        return document[..afterStart] + newline + contents.ReplaceLineEndings(newline) + newline + document[end..];
    }

    /// <summary>The block between two markers, markers excluded and line endings normalised, or null when
    /// there is no such block.</summary>
    public static string? ReadBlock(string document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);

        int start = document.IndexOf($"<!-- {name}:start", StringComparison.Ordinal);
        int end = document.IndexOf($"<!-- {name}:end", StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            return null;
        }

        int afterStart = document.IndexOf("-->", start, StringComparison.Ordinal);
        return afterStart < 0 || afterStart > end
            ? null
            : document[(afterStart + "-->".Length)..end].Trim('\r', '\n').ReplaceLineEndings("\n");
    }

    /// <summary>The repository this binary was built inside, found by the solution file above it.</summary>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Silueta.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Run this from inside the repository: no Silueta.slnx above the binary.");
    }
}
