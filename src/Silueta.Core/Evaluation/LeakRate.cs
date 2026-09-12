using System.Globalization;

namespace Silueta.Core;

/// <summary>
/// One transcript's result against its hand-annotated truth, counted in characters.
/// <para>
/// Characters rather than spans, because spans let a scorer lie in both directions. A detection that
/// clipped half a name used to count as a cover — gold "Sofía Reyes", redactor reached "Sofía", recall
/// 1.0, and "Reyes" still in the file. And one detection swallowing the whole document used to count as
/// zero over-redaction, because it overlapped the only gold span there was. Neither is possible when the
/// unit is a character and overlapping spans are unioned before anything is counted.
/// </para>
/// </summary>
/// <param name="SensitiveCharacters">How many characters the annotators marked.</param>
/// <param name="CoveredCharacters">How many of those the redactor both found and actually replaced.</param>
/// <param name="OverRedactedCharacters">Characters removed that the annotators never marked.</param>
/// <param name="SurvivingSpans">Annotated values still present, word for word, in the redacted text.
/// These are leaks no offset arithmetic can see: the span was found, replaced, and the replacement was
/// the name.</param>
public sealed record DeidScore(
    int SensitiveCharacters,
    int CoveredCharacters,
    int OverRedactedCharacters,
    int SurvivingSpans)
{
    /// <summary>Marked characters the redactor did not remove. One is enough to identify a document.</summary>
    public int MissedCharacters => SensitiveCharacters - CoveredCharacters;

    /// <summary>Share of sensitive characters removed. For tuning a detector, never for a headline.</summary>
    public double Recall => SensitiveCharacters == 0 ? 1.0 : (double)CoveredCharacters / SensitiveCharacters;

    /// <summary>Of everything removed, how much needed removing.</summary>
    public double Precision => CoveredCharacters + OverRedactedCharacters == 0
        ? 1.0
        : (double)CoveredCharacters / (CoveredCharacters + OverRedactedCharacters);

    /// <summary>Whether this transcript still identifies someone. One surviving name is enough.</summary>
    public bool Leaked => MissedCharacters > 0 || SurvivingSpans > 0;
}

/// <summary>
/// A leak rate and the corpus it was measured on, which travel together on purpose: a rate without its
/// denominator is the kind of number this library exists to stop people publishing.
/// </summary>
public sealed record LeakRateEstimate
{
    public LeakRateEstimate(int transcripts, int leaking)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(transcripts, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(leaking);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(leaking, transcripts);

        Transcripts = transcripts;
        Leaking = leaking;
    }

    public int Transcripts { get; }

    public int Leaking { get; }

    public double Rate => (double)Leaking / Transcripts;

    public override string ToString() =>
        $"{Rate.ToString("P1", CultureInfo.InvariantCulture).Replace(" ", string.Empty)} of {Transcripts} transcripts";
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
    /// Scores one transcript against its annotation.
    /// <para>
    /// It takes both texts, and that is the change that matters. A scorer given only offsets is checking
    /// that a detector fired, which is not the question: the question is whether the words are gone.
    /// A span can be detected, replaced, counted, and still be there afterwards — because the surrogate
    /// equalled the original, or because the same name was said again somewhere nobody annotated. Both
    /// are leaks, both were invisible, and both are caught by reading the output.
    /// </para>
    /// </summary>
    /// <param name="original">The transcript as it went in.</param>
    /// <param name="redacted">The transcript as it came out.</param>
    /// <param name="gold">Spans the annotators marked, as offsets into <paramref name="original"/>.</param>
    /// <param name="found">Spans the redactor replaced, as offsets into <paramref name="original"/>.</param>
    public static DeidScore Score(string original, string redacted, IEnumerable<Detection> gold, IEnumerable<Detection> found)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(redacted);

        List<(int Start, int End)> goldRanges = Union(gold, original.Length);
        List<(int Start, int End)> foundRanges = Union(found, original.Length);

        int sensitive = goldRanges.Sum(range => range.End - range.Start);
        int covered = IntersectionLength(goldRanges, foundRanges);
        int removed = foundRanges.Sum(range => range.End - range.Start);

        int surviving = 0;
        foreach ((int start, int end) in goldRanges)
        {
            string value = original[start..end].Trim();

            // Deliberately generous about what counts as surviving: any occurrence anywhere in the
            // output, ignoring case. A false alarm costs someone a second look. The other kind of
            // mistake costs a person.
            if (value.Length > 0 && redacted.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                surviving++;
            }
        }

        return new DeidScore(sensitive, covered, removed - covered, surviving);
    }

    /// <summary>
    /// The share of transcripts with at least one leak. Report this one first.
    /// <para>
    /// Null for an empty corpus, and that is the whole point of the return type. It used to return 0.0,
    /// which any report would print as a perfect score for a run that measured nothing.
    /// </para>
    /// </summary>
    public static LeakRateEstimate? OfTranscripts(IEnumerable<DeidScore> scores)
    {
        List<DeidScore> all = scores.ToList();
        return all.Count == 0 ? null : new LeakRateEstimate(all.Count, all.Count(score => score.Leaked));
    }

    /// <summary>Recall pooled over every sensitive character in the corpus, for tuning a detector.
    /// Null when there was nothing to measure.</summary>
    public static double? PooledRecall(IEnumerable<DeidScore> scores)
    {
        List<DeidScore> all = scores.ToList();
        if (all.Count == 0)
        {
            return null;
        }

        int sensitive = all.Sum(score => score.SensitiveCharacters);
        int covered = all.Sum(score => score.CoveredCharacters);
        return sensitive == 0 ? 1.0 : (double)covered / sensitive;
    }

    /// <summary>
    /// Merges spans into disjoint ranges. Two detectors finding the same name is the normal case, not the
    /// exception, and counting it twice inflates coverage and over-redaction alike.
    /// </summary>
    private static List<(int Start, int End)> Union(IEnumerable<Detection> spans, int limit)
    {
        List<(int Start, int End)> sorted = spans
            .Select(span => (Start: Math.Max(0, span.Start), End: Math.Min(limit, span.End)))
            .Where(range => range.End > range.Start)
            .OrderBy(range => range.Start)
            .ToList();

        var merged = new List<(int Start, int End)>();
        foreach ((int start, int end) in sorted)
        {
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            }
            else
            {
                merged.Add((start, end));
            }
        }

        return merged;
    }

    /// <summary>How many characters two sets of disjoint, sorted ranges have in common.</summary>
    private static int IntersectionLength(List<(int Start, int End)> left, List<(int Start, int End)> right)
    {
        int total = 0;
        int i = 0;
        int j = 0;

        while (i < left.Count && j < right.Count)
        {
            int start = Math.Max(left[i].Start, right[j].Start);
            int end = Math.Min(left[i].End, right[j].End);

            if (end > start)
            {
                total += end - start;
            }

            if (left[i].End < right[j].End)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return total;
    }
}
