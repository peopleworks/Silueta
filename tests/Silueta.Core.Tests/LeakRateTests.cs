using Silueta.Core;

namespace Silueta.Core.Tests;

public class LeakRateTests
{
    private static Detection Span(int start, int length) =>
        new(start, length, IdentifierKind.PatientName, new string('x', length), "gold", 1.0);

    [Fact]
    public void A_covered_span_counts_as_found()
    {
        DeidScore score = LeakRate.Score([Span(10, 5)], [Span(8, 9)]);

        Assert.Equal(1, score.Matched);
        Assert.Equal(0, score.Missed);
        Assert.False(score.Leaked);
    }

    [Fact]
    public void One_surviving_name_makes_the_transcript_a_leak()
    {
        DeidScore score = LeakRate.Score([Span(10, 5), Span(40, 6)], [Span(10, 5)]);

        Assert.Equal(1, score.Missed);
        Assert.True(score.Leaked);
        Assert.Equal(0.5, score.Recall);
    }

    [Fact]
    public void Removing_what_nobody_marked_is_over_redaction()
    {
        DeidScore score = LeakRate.Score([Span(10, 5)], [Span(10, 5), Span(90, 4)]);

        Assert.Equal(1, score.Extra);
        Assert.False(score.Leaked);
        Assert.Equal(0.5, score.Precision);
    }

    [Fact]
    public void The_headline_number_is_transcripts_not_mentions()
    {
        // Four transcripts, one leak between them: 97% of mentions removed, 25% of transcripts leaking.
        DeidScore[] scores =
        [
            new(30, 0, 0),
            new(29, 1, 0),
            new(35, 0, 0),
            new(28, 0, 0),
        ];

        Assert.Equal(0.25, LeakRate.OfTranscripts(scores));
        Assert.True(LeakRate.PooledRecall(scores) > 0.99);
    }
}
