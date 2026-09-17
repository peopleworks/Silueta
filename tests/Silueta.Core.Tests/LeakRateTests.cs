using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The measurement the whole library is supposed to rest on. Every case here is one the old scorer got
/// wrong: it counted any overlap as a cover, it counted spans instead of characters, and it never once
/// looked at the text it was scoring — so it could report a clean transcript that still said the name.
/// </summary>
public class LeakRateTests
{
    private static Detection Span(int start, int length) =>
        new(start, length, IdentifierKind.PatientName, "gold", 1.0);

    [Fact]
    public void Half_a_name_covered_is_a_name_leaked()
    {
        // Codex's reproduction. Gold "Sofía Reyes" at [0,11); the redactor reached [0,6). The old scorer
        // said Recall 1.0 and Leaked false, with "Reyes" still sitting in the output.
        const string original = "Sofía Reyes was on shift.";
        const string redacted = "Ale Reyes was on shift.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 11)], [Span(0, 6)]);

        Assert.True(score.Leaked);
        Assert.Equal(5, score.MissedCharacters); // "Reyes"
        Assert.True(score.Recall < 1.0);
    }

    [Fact]
    public void A_surrogate_that_equals_the_original_is_a_leak()
    {
        // The span was found, replaced, and counted — and the replacement was the name itself. Nothing
        // that looks only at offsets can see this; the scorer has to read the output.
        const string original = "Ale Espinal rested well.";
        const string redacted = "Ale Espinal rested well.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 11)], [Span(0, 11)]);

        Assert.True(score.Leaked);
    }

    [Fact]
    public void A_name_that_survives_somewhere_else_is_still_a_leak()
    {
        // Annotated once, said twice. The annotated mention was removed; the transcript still names her.
        const string original = "Eleanor rested. Later Eleanor called.";
        const string redacted = "Ale rested. Later Eleanor called.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 7)], [Span(0, 7)]);

        Assert.True(score.Leaked);
    }

    [Fact]
    public void A_span_that_is_really_covered_and_really_replaced_is_clean()
    {
        const string original = "Eleanor Vasquez rested well.";
        const string redacted = "Ale Espinal rested well.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 15)], [Span(0, 15)]);

        Assert.False(score.Leaked);
        Assert.Equal(0, score.MissedCharacters);
        Assert.Equal(15, score.CoveredCharacters);
        Assert.Equal(1.0, score.Recall);
    }

    [Fact]
    public void Two_detectors_covering_one_name_between_them_cover_it()
    {
        const string original = "Eleanor Vasquez rested well.";
        const string redacted = "[PATIENT][FAMILY] rested well.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 15)], [Span(0, 7), Span(7, 8)]);

        Assert.False(score.Leaked);
        Assert.Equal(15, score.CoveredCharacters);
    }

    [Fact]
    public void Redacting_the_whole_document_is_not_a_perfect_score()
    {
        // The degenerate strategy: remove everything. The old scorer gave it Extra == 0 and Recall 1.0,
        // because one giant span overlapped the only gold span.
        const string original = "Eleanor Vasquez rested well and ate breakfast with her daughter.";
        string redacted = "[PATIENT]";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 15)], [Span(0, original.Length)]);

        Assert.False(score.Leaked);
        Assert.Equal(original.Length - 15, score.OverRedactedCharacters);
        Assert.True(score.Precision < 0.25, $"precision was {score.Precision:0.000}");
    }

    [Fact]
    public void Removing_what_nobody_marked_is_over_redaction()
    {
        const string original = "Eleanor rested well and ate breakfast.";
        const string redacted = "[PATIENT] rested well and ate [REMOVED].";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 7)], [Span(0, 7), Span(28, 9)]);

        Assert.Equal(9, score.OverRedactedCharacters);
        Assert.False(score.Leaked);
    }

    [Fact]
    public void Overlapping_detections_are_not_counted_twice()
    {
        const string original = "Eleanor Vasquez rested well.";
        const string redacted = "[PATIENT] rested well.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 15)], [Span(0, 15), Span(0, 15), Span(3, 9)]);

        Assert.Equal(15, score.CoveredCharacters);
        Assert.Equal(0, score.OverRedactedCharacters);
    }

    [Fact]
    public void One_surviving_name_makes_the_transcript_a_leak()
    {
        const string original = "Eleanor rested. Sofia called at noon.";
        const string redacted = "Ale rested. Sofia called at noon.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 7), Span(16, 5)], [Span(0, 7)]);

        Assert.True(score.Leaked);
        Assert.Equal(5, score.MissedCharacters);
        Assert.Equal(7.0 / 12.0, score.Recall, 3);
    }

    [Fact]
    public void An_empty_corpus_has_no_leak_rate_rather_than_a_leak_rate_of_zero()
    {
        // The old signature returned 0.0 here, which reads as "measured, and perfect". Nothing was
        // measured. A privacy library that reports a clean score for an empty run has said the one
        // thing it must never say.
        Assert.Null(LeakRate.OfTranscripts([]));
        Assert.Null(LeakRate.PooledRecall([]));
    }

    [Fact]
    public void The_headline_number_is_transcripts_not_mentions()
    {
        const string clean = "Nothing identifying here at all.";
        DeidScore[] scores =
        [
            LeakRate.Score(clean, clean, [], []),
            LeakRate.Score("Eleanor rested.", "Eleanor rested.", [Span(0, 7)], []),
            LeakRate.Score(clean, clean, [], []),
            LeakRate.Score(clean, clean, [], []),
        ];

        LeakRateEstimate? estimate = LeakRate.OfTranscripts(scores);

        Assert.NotNull(estimate);
        Assert.Equal(0.25, estimate.Rate);
        Assert.Equal(4, estimate.Transcripts);
        Assert.Equal(1, estimate.Leaking);
    }

    [Fact]
    public void A_leak_rate_always_carries_the_corpus_it_was_measured_on()
    {
        // A number without its denominator is the thing this project exists to stop publishing.
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeakRateEstimate(0, 0));

        // And, since Phase 1, its interval: with forty documents three leaks is a rate that could be anywhere
        // from one in forty to one in five, and a printed rate that hides that is a claim of precision the
        // corpus cannot support.
        LeakRateEstimate estimate = new(40, 3);
        Assert.Equal("7.5% of 40 transcripts (95% CI 2.6%–19.9%)", estimate.ToString());
    }

    [Fact]
    public void A_name_inside_a_longer_annotated_span_is_checked_on_its_own()
    {
        // Two annotators marking the same passage at different granularities is the normal case, and
        // Phase 1 requires two of them per document. Merging their spans before looking for survivors
        // asks "is 'Sofía Reyes' still here" — it is not — and never asks about "Reyes", which is.
        const string original = "Sofía Reyes rested well.";
        const string redacted = "Ale Reyes rested well.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 11), Span(6, 5)], [Span(0, 11)]);

        Assert.True(score.Leaked);
        Assert.Equal(1, score.SurvivingSpans);
    }

    [Fact]
    public void Dropping_the_accents_off_a_name_is_not_a_redaction()
    {
        // Detection.cs defines an exact match as "letter for letter (ignoring case and accents)", and
        // PhoneticKey strips accents before it does anything else. The survival check was the one place
        // that compared ordinally, so the library's own detector could find every identifier in a text
        // this meter scored at recall 1.00, precision 1.00, no leak.
        const string original = "José Martínez llamó a Sofía Reyes.";
        const string redacted = "Jose Martinez llamo a Sofia Reyes.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(0, 14), Span(22, 11)], [Span(0, 14), Span(22, 11)]);

        Assert.True(score.Leaked);
        Assert.Equal(2, score.SurvivingSpans);
    }

    [Fact]
    public void Case_is_folded_too_and_in_the_same_direction()
    {
        const string original = "Sofía Reyes rested.";

        Assert.True(LeakRate.Score(original, "SOFIA REYES rested.", [Span(0, 11)], [Span(0, 11)]).Leaked);
        Assert.True(LeakRate.Score(original, "sofia reyes rested.", [Span(0, 11)], [Span(0, 11)]).Leaked);
    }

    [Fact]
    public void An_annotation_that_swept_up_a_full_stop_is_not_a_leak()
    {
        // Annotators select sloppily. If the punctuation at the edge of a span counts as an uncovered
        // sensitive character, a corpus of thirty annotations per document reports a leak in nearly
        // every document — and a meter that cries wolf is as useless as one that stays quiet.
        const string original = "La paciente es Eleanor Vasquez.";
        const string redacted = "La paciente es Dani Duarte.";

        DeidScore score = LeakRate.Score(original, redacted, [Span(15, 16)], [Span(15, 15)]);

        Assert.False(score.Leaked);
        Assert.Equal(0, score.MissedCharacters);
    }

    [Fact]
    public void A_transcript_with_nothing_to_find_cannot_leak()
    {
        const string text = "Blood pressure 138 over 82, pain 4 out of 10.";

        DeidScore score = LeakRate.Score(text, text, [], []);

        Assert.False(score.Leaked);
        Assert.Equal(1.0, score.Recall);
    }
}
