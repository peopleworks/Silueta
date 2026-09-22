using System.Globalization;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The published number, held to the repository.
/// <para>
/// The README and the skill both state a leak rate now, with its interval, and a number written in prose
/// drifts the day anything under it changes — a matcher fix, a pattern, a gold file. So this test runs the
/// evaluation on the committed corpus and requires both documents to contain exactly what it computed. The
/// README's demo block has had the same guard since the start; this is that guard for the number the project
/// exists to publish. It replaces the test that required the skill to say the number did not exist, which
/// Phase 1 was always going to delete, by hand.
/// </para>
/// </summary>
public class PublishedNumberTests
{
    private static readonly string Root = Repo.Root;

    private static EvaluationReport Evaluate() => Evaluation.Run(
        GoldCorpus.Load(Path.Combine(Root, "corpus-synthetic", "tts-asr")),
        EvaluationConfiguration.Silueta,
        EvaluationConfiguration.DenyList);

    private static string Block(string text, string name)
    {
        int start = text.IndexOf($"<!-- {name}:start", StringComparison.Ordinal);
        int end = text.IndexOf($"<!-- {name}:end", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"The README has no {name} block between markers.");
        return text[start..end];
    }

    private static string Recall(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    [Fact]
    public void The_README_states_the_numbers_a_fresh_evaluation_computes()
    {
        EvaluationReport report = Evaluate();
        ConfigurationResult silueta = report.Configurations.Single(c => c.Name == "silueta");
        ConfigurationResult literal = report.Configurations.Single(c => c.Name == "deny-list");
        PairedComparison thesis = report.Comparisons.Single();

        string block = Block(File.ReadAllText(Path.Combine(Root, "README.md")), "leak-rate");

        foreach (string expected in (string[])[
                     silueta.LeakRate.ToString(), literal.LeakRate.ToString(),
                     silueta.LeakRateInScope.ToString(), literal.LeakRateInScope.ToString(),
                     Recall(silueta.RecallInScope), Recall(literal.RecallInScope),
                     thesis.RecallDifference.ToString(), thesis.LeakRateDifference.ToString(),
                     report.Lineage, report.EngineVersion[..report.EngineVersion.LastIndexOf('.')],
                     $"{report.Corpus.Documents} documents"])
        {
            Assert.True(block.Contains(expected, StringComparison.Ordinal),
                $"The README's published block does not say \"{expected}\", which is what the evaluation computes now.");
        }
    }

    [Fact]
    public void The_skill_names_the_number_with_its_interval_and_what_it_was_measured_on()
    {
        EvaluationReport report = Evaluate();
        ConfigurationResult silueta = report.Configurations.Single(c => c.Name == "silueta");
        string skill = File.ReadAllText(Path.Combine(Root, "SKILL.md"));

        Assert.Contains(silueta.LeakRateInScope.ToString(), skill, StringComparison.Ordinal);
        Assert.Contains(silueta.LeakRate.ToString(), skill, StringComparison.Ordinal);
        Assert.Contains("synthetic", skill, StringComparison.OrdinalIgnoreCase);
    }
}
