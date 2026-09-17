using System.Text.Json;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The evaluator, starting from the one document whose right answer is already written down in public.
/// <para>
/// The README shows the demo's output and says, in prose, that "Rays" and "Ellie" survive it. That makes the
/// demo transcript the evaluator's own canary: annotated by hand as <c>corpus-synthetic/readme-demo/gold-001.json</c>,
/// scored by the same code that will score the real corpus, it has to come back leaked — through exactly
/// the staff surname and the nickname, and through nothing else. If the evaluator says anything different,
/// the evaluator is wrong, and every number it prints later would be too.
/// </para>
/// </summary>
public class EvaluationTests
{
    private static string CorpusRoot => Path.Combine(McpToolDocumentationTests.RepoRoot, "corpus-synthetic");

    private static GoldCorpus Demo() => GoldCorpus.Load(Path.Combine(CorpusRoot, "readme-demo"));

    [Fact]
    public void The_demo_leaks_through_exactly_the_two_names_the_README_says_survive()
    {
        EvaluationReport report = Evaluation.Run(Demo(), EvaluationConfiguration.Silueta);

        ConfigurationResult silueta = report.Configurations.Single();
        Assert.Equal(1, silueta.LeakRate.Transcripts);
        Assert.Equal(1, silueta.LeakRate.Leaking);

        DocumentResult document = silueta.Documents.Single();
        Assert.True(document.Score.Leaked);

        // "Rays", the staff surname the matcher failed on, and "Ellie", the nickname nothing knows.
        Assert.True(document.Score.ByKind[IdentifierKind.StaffName].Covered < document.Score.ByKind[IdentifierKind.StaffName].Sensitive);
        Assert.True(document.Score.ByKind[IdentifierKind.PatientName].SurvivingSpans >= 1);

        // Everything else in the demo is gone.
        foreach (IdentifierKind kind in (IdentifierKind[])[IdentifierKind.FamilyName, IdentifierKind.Phone,
                     IdentifierKind.Email, IdentifierKind.Date, IdentifierKind.AgeOver89])
        {
            KindScore score = document.Score.ByKind[kind];
            Assert.True(score.Covered == score.Sensitive, $"{kind} was not fully covered in the demo.");
        }
    }

    [Fact]
    public void The_exact_roster_baseline_misses_what_the_recogniser_damaged()
    {
        // The difference between this baseline and Silueta is the value of the phonetic thesis, measured.
        // On the demo, a literal roster match finds none of "Ellenor Vasques", "Jamileth" or "Sophia" —
        // they are not spelled the way the agency spells them.
        EvaluationReport report = Evaluation.Run(Demo(), EvaluationConfiguration.Silueta, EvaluationConfiguration.DenyList);

        DeidScore silueta = report.Configurations.Single(c => c.Name == "silueta").Documents.Single().Score;
        DeidScore denyList = report.Configurations.Single(c => c.Name == "deny-list").Documents.Single().Score;

        Assert.True(denyList.ByKind[IdentifierKind.PatientName].Covered < silueta.ByKind[IdentifierKind.PatientName].Covered);
        Assert.Equal(0, denyList.ByKind[IdentifierKind.FamilyName].Covered);
        Assert.True(denyList.MissedCharacters > silueta.MissedCharacters);
    }

    [Fact]
    public void Ablation_runs_each_detector_on_its_own()
    {
        EvaluationReport report = Evaluation.Run(
            Demo(), EvaluationConfiguration.Silueta, EvaluationConfiguration.KnownValuesOnly, EvaluationConfiguration.PatternsOnly);

        DeidScore patternsOnly = report.Configurations.Single(c => c.Name == "patterns-only").Documents.Single().Score;
        DeidScore knownOnly = report.Configurations.Single(c => c.Name == "known-values-only").Documents.Single().Score;

        Assert.Equal(0, patternsOnly.ByKind[IdentifierKind.FamilyName].Covered);
        Assert.Equal(patternsOnly.ByKind[IdentifierKind.Phone].Sensitive, patternsOnly.ByKind[IdentifierKind.Phone].Covered);
        Assert.Equal(0, knownOnly.ByKind[IdentifierKind.Phone].Covered);
    }

    [Fact]
    public void The_report_carries_no_identifier_from_the_corpus()
    {
        // Evaluate will be run on a corpus under NDA. Offsets and kinds, never values — the same rule the
        // manifest follows, carried into the one file most likely to be emailed around.
        EvaluationReport report = Evaluation.Run(Demo(), EvaluationConfiguration.Silueta, EvaluationConfiguration.DenyList);

        string json = JsonSerializer.Serialize(report);
        foreach (string value in (string[])["Ellie", "Rays", "Jamileth", "602-555-0147", "Vasques", "Sophia"])
        {
            Assert.DoesNotContain(value, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_report_says_how_the_corpus_was_annotated()
    {
        EvaluationReport report = Evaluation.Run(Demo(), EvaluationConfiguration.Silueta);

        Assert.Equal(1, report.Corpus.Documents);
        Assert.Contains("readme-demo", report.Corpus.Sources.Keys);
        Assert.Equal(0, report.Corpus.DocumentsWithTwoAnnotators);
        Assert.Contains(report.Corpus.Caveats, c => c.Contains("one annotator", StringComparison.OrdinalIgnoreCase));
    }
}
