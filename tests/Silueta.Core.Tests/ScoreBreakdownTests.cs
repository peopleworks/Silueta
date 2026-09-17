using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// A score that says which identifier failed, and which detector did the work.
/// <para>
/// <c>DeidScore</c> was four integers. "A name leaked" and "a phone number leaked" produced two results that
/// differed only by the length of the string, and nothing said which of the identifiers failed — while
/// ALGORITHM promised recall per identifier kind and Phase 1 lists it as a deliverable. Recall per kind could
/// have been recovered by splitting the annotations and scoring N times; precision and over-redaction per
/// kind could not, because in a split call everything removed for the other kinds counts as over-redaction
/// of this one.
/// </para>
/// <para>
/// Two attribution rules, chosen and not defaulted. What was sensitive, covered or left behind is counted
/// under the kind the <b>annotators</b> gave it — a name the detector called OtherName is still a patient
/// name that was covered. What was removed without need is counted under the kind the <b>detector</b> gave
/// it, because that is the rule that fired. And when two annotations of different kinds overlap, the shared
/// characters count under both kinds, so the per-kind figures can add up to more than the total.
/// </para>
/// </summary>
public class ScoreBreakdownTests
{
    private static Detection Gold(int start, int length, IdentifierKind kind) =>
        new(start, length, kind, "gold", 1.0);

    private static Detection Found(int start, int length, IdentifierKind kind, string detector = "known-value") =>
        new(start, length, kind, detector, 0.9);

    [Fact]
    public void A_score_says_which_kind_of_identifier_was_missed()
    {
        const string original = "Eleanor called 602-555-0147.";
        const string redacted = "Eleanor called [PHONE].";

        DeidScore score = LeakRate.Score(original, redacted,
            [Gold(0, 7, IdentifierKind.PatientName), Gold(15, 12, IdentifierKind.Phone)],
            [Found(15, 12, IdentifierKind.Phone, "pattern:phone")]);

        Assert.Equal(7, score.ByKind[IdentifierKind.PatientName].Sensitive);
        Assert.Equal(0, score.ByKind[IdentifierKind.PatientName].Covered);
        Assert.Equal(1, score.ByKind[IdentifierKind.PatientName].SurvivingSpans);
        Assert.Equal(12, score.ByKind[IdentifierKind.Phone].Covered);
        Assert.Equal(0, score.ByKind[IdentifierKind.Phone].SurvivingSpans);
    }

    [Fact]
    public void Coverage_is_counted_under_the_annotators_kind_even_when_the_detector_called_it_something_else()
    {
        const string original = "Eleanor rested.";
        const string redacted = "Ale rested.";

        DeidScore score = LeakRate.Score(original, redacted,
            [Gold(0, 7, IdentifierKind.PatientName)],
            [Found(0, 7, IdentifierKind.OtherName)]);

        Assert.Equal(7, score.ByKind[IdentifierKind.PatientName].Covered);

        // And the detector's kind is not charged with over-redaction for removing a real identifier.
        Assert.False(score.ByKind.TryGetValue(IdentifierKind.OtherName, out KindScore? other) && other.OverRedacted > 0);
    }

    [Fact]
    public void Over_redaction_is_counted_under_the_detectors_kind()
    {
        const string original = "Pain 4 out of 10 on 3/14.";
        const string redacted = "Pain 4 out of 10 on [DATE].";

        DeidScore score = LeakRate.Score(original, redacted, [], [Found(20, 4, IdentifierKind.Date, "pattern:date")]);

        Assert.Equal(4, score.ByKind[IdentifierKind.Date].OverRedacted);
        Assert.Equal(0, score.ByKind[IdentifierKind.Date].Sensitive);
    }

    [Fact]
    public void Overlapping_annotations_of_two_kinds_count_under_both_and_the_parts_can_exceed_the_whole()
    {
        // One annotator marked "Eleanor Vasquez" as the patient; the other marked "Eleanor" alone as a name
        // of unknown role. The seven shared characters are sensitive under both kinds.
        const string original = "Eleanor Vasquez rested.";

        DeidScore score = LeakRate.Score(original, original,
            [Gold(0, 15, IdentifierKind.PatientName), Gold(0, 7, IdentifierKind.OtherName)],
            []);

        Assert.Equal(15, score.SensitiveCharacters);
        Assert.Equal(15, score.ByKind[IdentifierKind.PatientName].Sensitive);
        Assert.Equal(7, score.ByKind[IdentifierKind.OtherName].Sensitive);
        Assert.True(score.ByKind.Values.Sum(k => k.Sensitive) > score.SensitiveCharacters);
    }

    [Fact]
    public void A_score_says_what_each_detector_contributed()
    {
        const string original = "Eleanor called on 3/14 about pain 4 out of 10.";
        const string redacted = "Ale called on 2026 about pain [NUMBER].";

        DeidScore score = LeakRate.Score(original, redacted,
            [Gold(0, 7, IdentifierKind.PatientName), Gold(18, 4, IdentifierKind.Date)],
            [
                Found(0, 7, IdentifierKind.PatientName, "known-value"),
                Found(18, 4, IdentifierKind.Date, "pattern:date"),
                Found(34, 11, IdentifierKind.Other, "pattern:number"),
            ]);

        Assert.Equal(7, score.ByDetector["known-value"].Covered);
        Assert.Equal(4, score.ByDetector["pattern:date"].Covered);
        Assert.Equal(0, score.ByDetector["pattern:number"].Covered);
        Assert.Equal(11, score.ByDetector["pattern:number"].OverRedacted);
    }

    [Fact]
    public void The_totals_did_not_move()
    {
        // The breakdown is new information about the same measurement, not a new measurement.
        const string original = "Eleanor Vasquez called 602-555-0147.";
        const string redacted = "Ale Vasquez called [PHONE].";

        DeidScore score = LeakRate.Score(original, redacted,
            [Gold(0, 15, IdentifierKind.PatientName), Gold(23, 12, IdentifierKind.Phone)],
            [Found(0, 7, IdentifierKind.PatientName), Found(23, 12, IdentifierKind.Phone, "pattern:phone")]);

        Assert.Equal(27, score.SensitiveCharacters);
        Assert.Equal(19, score.CoveredCharacters);
        Assert.True(score.Leaked);
    }

    [Theory]
    [InlineData(40, 3, 0.0258, 0.1986)]
    [InlineData(30, 0, 0.0, 0.1135)]
    [InlineData(30, 30, 0.8865, 1.0)]
    public void A_leak_rate_carries_its_interval(int transcripts, int leaking, double lower, double upper)
    {
        // Wilson, not the textbook normal interval: with thirty documents and no leaks the normal interval
        // is [0%, 0%], which prints as a proven zero. Wilson says the rate could still be one in nine.
        LeakRateEstimate estimate = new(transcripts, leaking);

        Assert.Equal(lower, estimate.Lower, precision: 3);
        Assert.Equal(upper, estimate.Upper, precision: 3);
    }
}
