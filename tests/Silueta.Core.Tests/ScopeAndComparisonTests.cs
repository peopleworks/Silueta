using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Two numbers that must not be confused, and the one that decides whether the thesis is worth anything.
/// <para>
/// The leak rate over every kind the annotators marked answers "can this corpus leave the building?". It
/// includes kinds no rule looks for and people no roster names, so it measures the absence of a rule as much
/// as the matcher. The leak rate over the kinds this build has a way to find — a pattern rule, or the roster —
/// judges the matcher. A kind with a rule that fails stays in scope: the scope comes from what the build
/// attempts, not from what it gets right. And the difference between Silueta and the same roster matched
/// literally, over the same documents, is what the phonetic thesis is worth, with an interval.
/// </para>
/// </summary>
public class ScopeAndComparisonTests
{
    private static GoldCorpus Synthetic() =>
        GoldCorpus.Load(Path.Combine(Repo.Root, "corpus-synthetic", "tts-asr"));

    [Fact]
    public void The_pattern_pack_says_which_kinds_it_has_rules_for()
    {
        IReadOnlyCollection<IdentifierKind> kinds = PatternDetector.FromEmbeddedPack().Kinds;

        Assert.Contains(IdentifierKind.Phone, kinds);
        Assert.Contains(IdentifierKind.RecordNumber, kinds);
        Assert.Contains(IdentifierKind.Address, kinds);
        Assert.DoesNotContain(IdentifierKind.DeviceId, kinds);
        Assert.DoesNotContain(IdentifierKind.PatientName, kinds);
    }

    private static ConfigurationResult ScoreOne(string text, string spansJson)
    {
        string directory = Directory.CreateTempSubdirectory("silueta-scope-").FullName;
        try
        {
            File.WriteAllText(System.IO.Path.Combine(directory, "d.json"), $$"""
                { "documentId": "d", "source": "s", "text": "{{text}}",
                  "roster": [ { "value": "Eleanor Vasquez", "kind": "PatientName", "subjectId": "p-1" } ],
                  "spans": {{spansJson}} }
                """);

            return Evaluation.Run(GoldCorpus.Load(directory), EvaluationConfiguration.Silueta).Configurations.Single();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_kind_no_rule_looks_for_leaks_the_corpus_but_not_the_matcher()
    {
        // A device serial number: no rule looks for one, so its survival says nothing about the matcher.
        ConfigurationResult silueta = ScoreOne("Eleanor has pump PX-4471.", """
            [ { "start": 0, "length": 7, "kind": "PatientName", "annotator": "a" },
              { "start": 17, "length": 7, "kind": "DeviceId", "annotator": "a" } ]
            """);

        Assert.Equal(1, silueta.LeakRate.Leaking);
        Assert.Equal(0, silueta.LeakRateInScope.Leaking);
        Assert.False(silueta.Documents.Single().LeakedInScope);
    }

    [Fact]
    public void A_place_the_address_rules_miss_counts_against_the_build()
    {
        // This test used to be the one above, with a town: "Mesa" was a place no rule looked for, so it leaked the
        // corpus and not the matcher. Since the address rules exist, Address is in scope, and a town named alone —
        // which they cannot see — is a miss of this build. A rule that exists and fails stays in scope.
        ConfigurationResult silueta = ScoreOne("Eleanor lives in Mesa.", """
            [ { "start": 0, "length": 7, "kind": "PatientName", "annotator": "a" },
              { "start": 17, "length": 4, "kind": "Address", "annotator": "a" } ]
            """);

        Assert.Equal(1, silueta.LeakRateInScope.Leaking);
        Assert.True(silueta.Documents.Single().LeakedInScope);
    }

    [Fact]
    public void Inside_the_scope_is_never_worse_than_the_whole_on_any_document()
    {
        foreach (ConfigurationResult configuration in Evaluation.Run(Synthetic(), EvaluationConfiguration.Silueta, EvaluationConfiguration.DenyList).Configurations)
        {
            Assert.All(configuration.Documents, d => Assert.True(!d.LeakedInScope || d.Score.Leaked, d.DocumentId));
        }
    }

    [Fact]
    public void Comparing_a_configuration_with_itself_is_a_difference_of_nothing_with_no_width()
    {
        EvaluationReport report = Evaluation.Run(Synthetic(), EvaluationConfiguration.Silueta, EvaluationConfiguration.Silueta);

        PairedComparison comparison = Evaluation.Compare(report.Configurations[0], report.Configurations[1]);

        Assert.Equal(0.0, comparison.RecallDifference.Estimate);
        Assert.Equal(0.0, comparison.RecallDifference.Lower);
        Assert.Equal(0.0, comparison.RecallDifference.Upper);
        Assert.Equal(0.0, comparison.LeakRateDifference.Estimate);
    }

    [Fact]
    public void The_bootstrap_is_reproducible_from_its_seed()
    {
        EvaluationReport report = Evaluation.Run(Synthetic(), EvaluationConfiguration.Silueta, EvaluationConfiguration.DenyList);

        PairedComparison first = Evaluation.Compare(report.Configurations[0], report.Configurations[1]);
        PairedComparison second = Evaluation.Compare(report.Configurations[0], report.Configurations[1]);

        Assert.Equal(first, second);
        Assert.Equal(20260916, first.Seed);
        Assert.Equal(1000, first.Resamples);
    }

    [Fact]
    public void The_report_carries_the_comparison_when_both_sides_ran()
    {
        EvaluationReport report = Evaluation.Run(Synthetic(), EvaluationConfiguration.Silueta, EvaluationConfiguration.DenyList);

        PairedComparison comparison = Assert.Single(report.Comparisons);
        Assert.Equal("silueta", comparison.Configuration);
        Assert.Equal("deny-list", comparison.Baseline);
    }
}
