using System.Globalization;
using System.Text.Json;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The number, carried by the build instead of by a document.
/// <para>
/// <see cref="PublishedNumberTests"/> holds the README and the skill to a fresh evaluation, which keeps the
/// prose honest for anyone reading the repository. It does nothing for the person who installed the package:
/// a manifest arriving in somebody's inbox says what ran and not how often what ran is wrong. So the same
/// measurement is embedded in the assembly, and this is what stops the embedded copy from drifting away from
/// the corpus that is committed beside it.
/// </para>
/// <para>
/// The comparison is against a fresh run, never against constants written here. A test that repeated the
/// numbers would agree with a stale resource, which is the one failure worth catching: the file is written by
/// <c>tools/Silueta.Calibration</c> and embedded at build time, so "the tool was not re-run" and "the tool was
/// re-run and nothing changed" look identical from the outside.
/// </para>
/// </summary>
public class PublishedLeakRateTests
{
    private static readonly string Root = Repo.Root;

    private static EvaluationReport Evaluate() => Evaluation.Run(
        GoldCorpus.Load(Path.Combine(Root, "corpus-synthetic", "tts-asr")),
        EvaluationConfiguration.Silueta,
        EvaluationConfiguration.DenyList);

    [Fact]
    public void This_build_carries_its_own_measured_leak_rate()
    {
        Assert.NotNull(PublishedLeakRate.Current);
    }

    [Fact]
    public void What_the_build_carries_is_what_a_fresh_evaluation_computes()
    {
        PublishedLeakRate published = Assert.IsType<PublishedLeakRate>(PublishedLeakRate.Current);

        // The date is taken from the embedded file rather than from the clock: what this test checks is that
        // the numbers still hold, and a run tomorrow must not fail because the calendar moved.
        PublishedLeakRate fresh = PublishedLeakRate.From(Evaluate(), published.CorpusId, published.MeasuredOn);

        PublishedConfiguration silueta = Assert.IsType<PublishedConfiguration>(published.For("silueta"));
        PublishedConfiguration freshSilueta = Assert.IsType<PublishedConfiguration>(fresh.For("silueta"));

        // Named first so a failure says which number moved before it prints the whole file.
        Assert.Equal(freshSilueta.Leaking, silueta.Leaking);
        Assert.Equal(freshSilueta.LeakingInScope, silueta.LeakingInScope);
        Assert.Equal(freshSilueta.RecallInScope, silueta.RecallInScope, 12);
        Assert.Equal(fresh.Engine, published.Engine);
        Assert.Equal(fresh.LineageFingerprint, published.LineageFingerprint);
        Assert.Equal(fresh.PolicyFingerprint, published.PolicyFingerprint);

        // And then everything, including the fields nobody thought to name: the caveats, the sources, the
        // interval of the paired bootstrap. Records with collections in them do not compare by value, so the
        // comparison is on what the two serialise to — which is also the artefact that ships.
        Assert.Equal(
            JsonSerializer.Serialize(fresh, SiluetaJsonContext.Default.PublishedLeakRate),
            JsonSerializer.Serialize(published, SiluetaJsonContext.Default.PublishedLeakRate));
    }

    [Fact]
    public void The_embedded_file_and_the_README_agree_on_when_it_was_measured()
    {
        PublishedLeakRate published = Assert.IsType<PublishedLeakRate>(PublishedLeakRate.Current);

        string readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        int start = readme.IndexOf("<!-- leak-rate:start", StringComparison.Ordinal);
        int end = readme.IndexOf("<!-- leak-rate:end", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The README has no leak-rate block between markers.");

        string block = readme[start..end];
        string date = DateOnly.ParseExact(published.MeasuredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        Assert.True(
            block.Contains(date, StringComparison.Ordinal),
            $"The README's published block does not carry the date the embedded measurement was taken ({date}).");
        Assert.True(
            block.Contains($"{published.Documents} documents", StringComparison.Ordinal),
            "The README's published block does not carry the size of the corpus the embedded measurement used.");
    }

    [Fact]
    public void A_manifest_carries_what_the_build_measured_about_itself()
    {
        SiluetaEngine engine = SiluetaEngine.CreateDefault();
        RedactionResult result = engine.Redact(
            "Call 602-555-0147 about the visit.",
            new DeidentificationContext("rec-1"));

        Assert.Equal(PublishedLeakRate.Current?.Summary, result.Manifest.MeasuredLeakRate);
        Assert.Contains("of 30 transcripts", result.Manifest.MeasuredLeakRate, StringComparison.Ordinal);
    }

    [Fact]
    public void A_build_with_no_measurement_says_so_instead_of_leaving_the_field_empty()
    {
        // The default is the admission, not the empty string. Three times already in this project a required
        // field defaulted to something harmless-looking and the harmless value was the defect: a roster entry
        // with no kind became a person, an empty object parsed as a valid vault. A manifest whose leak rate is
        // missing reads as a run that did not leak, and this is the one field where that reading is worst.
        Assert.Contains("no measured leak rate", new RedactionManifest().MeasuredLeakRate, StringComparison.OrdinalIgnoreCase);
    }
}
