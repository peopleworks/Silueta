namespace Silueta.Core;

/// <summary>One transcript's result against its hand-annotated truth.</summary>
/// <param name="Matched">Gold spans that something covered.</param>
/// <param name="Missed">Gold spans nothing covered. These are the leaks.</param>
/// <param name="Extra">Spans removed that the annotators did not mark — over-redaction.</param>
public sealed record DeidScore(int Matched, int Missed, int Extra)
{
    public double Recall => Matched + Missed == 0 ? 1.0 : (double)Matched / (Matched + Missed);

    public double Precision => Matched + Extra == 0 ? 1.0 : (double)Matched / (Matched + Extra);

    /// <summary>Whether this transcript still identifies someone. One surviving name is enough.</summary>
    public bool Leaked => Missed > 0;
}

/// <summary>
/// The measurement that makes the rest defensible.
/// <para>
/// The headline number is not the share of identifiers removed — that one always looks good. It is the
/// share of <em>transcripts</em> with at least one identifier left, because a transcript with one
/// surviving name is an identified transcript. The arithmetic is unkind and that is the point: at 99%
/// recall per mention, a transcript with fifty mentions has a leak about 40% of the time.
/// </para>
/// </summary>
public static class LeakRate
{
    /// <summary>
    /// Any overlap counts as covered. Whether the kind was labelled correctly matters for tuning, not
    /// for the leak: what decides re-identification is whether the words survived.
    /// </summary>
    public static DeidScore Score(IEnumerable<Detection> gold, IEnumerable<Detection> found)
    {
        List<Detection> goldSpans = gold.ToList();
        List<Detection> foundSpans = found.ToList();

        int matched = 0;
        foreach (Detection expected in goldSpans)
        {
            if (foundSpans.Any(f => f.Overlaps(expected)))
            {
                matched++;
            }
        }

        int extra = foundSpans.Count(f => !goldSpans.Any(g => g.Overlaps(f)));
        return new DeidScore(matched, goldSpans.Count - matched, extra);
    }

    /// <summary>The share of transcripts with at least one leak. Report this one first.</summary>
    public static double OfTranscripts(IEnumerable<DeidScore> scores)
    {
        List<DeidScore> all = scores.ToList();
        return all.Count == 0 ? 0.0 : (double)all.Count(s => s.Leaked) / all.Count;
    }

    /// <summary>Recall pooled over every mention in the corpus, for tuning a detector.</summary>
    public static double PooledRecall(IEnumerable<DeidScore> scores)
    {
        List<DeidScore> all = scores.ToList();
        int matched = all.Sum(s => s.Matched);
        int missed = all.Sum(s => s.Missed);
        return matched + missed == 0 ? 1.0 : (double)matched / (matched + missed);
    }
}
