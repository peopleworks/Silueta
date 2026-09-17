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

    /// <summary>
    /// The same measurement, by kind of identifier.
    /// <para>
    /// What was sensitive, covered or left behind is counted under the kind the <b>annotators</b> gave it: a
    /// patient's name the detector called OtherName is still a patient's name that was covered. What was
    /// removed without need is counted under the kind the <b>detector</b> gave it, because that is the rule
    /// that fired. Where two annotations of different kinds overlap, the shared characters count under both,
    /// so the per-kind figures can add up to more than the totals — which stay the totals.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<IdentifierKind, KindScore> ByKind { get; init; } =
        System.Collections.Frozen.FrozenDictionary<IdentifierKind, KindScore>.Empty;

    /// <summary>What each detector covered and what it removed without need, by detector id. A character two
    /// detectors both covered counts for each; this is attribution, not ablation — ablation is a second run
    /// without the detector, and only that says what would have been lost.</summary>
    public IReadOnlyDictionary<string, DetectorScore> ByDetector { get; init; } =
        System.Collections.Frozen.FrozenDictionary<string, DetectorScore>.Empty;
}

/// <summary>One kind of identifier's share of a <see cref="DeidScore"/>.</summary>
public sealed record KindScore(int Sensitive, int Covered, int OverRedacted, int SurvivingSpans)
{
    public double Recall => Sensitive == 0 ? 1.0 : (double)Covered / Sensitive;
}

/// <summary>One detector's share of a <see cref="DeidScore"/>.</summary>
public sealed record DetectorScore(int Covered, int OverRedacted);

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

    /// <summary>Lower bound of the 95% Wilson score interval.</summary>
    public double Lower => Wilson().Lower;

    /// <summary>Upper bound of the 95% Wilson score interval.</summary>
    public double Upper => Wilson().Upper;

    public override string ToString() =>
        $"{Percent(Rate)} of {Transcripts} transcripts (95% CI {Percent(Lower)}–{Percent(Upper)})";

    /// <summary>
    /// Wilson, not the textbook normal interval. With thirty documents and no leaks the normal interval is
    /// [0%, 0%], which prints as a proven zero; Wilson says the true rate could still be one in nine. A
    /// corpus of this size cannot support more precision than that, and the number should say so itself.
    /// </summary>
    private (double Lower, double Upper) Wilson()
    {
        const double z = 1.959963984540054;
        double n = Transcripts;
        double p = Rate;
        double z2 = z * z;
        double denominator = 1 + (z2 / n);
        double centre = (p + (z2 / (2 * n))) / denominator;
        double half = z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4 * n * n))) / denominator;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    private static string Percent(double value) =>
        value.ToString("P1", CultureInfo.InvariantCulture).Replace(" ", string.Empty);
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

        // Annotators select sloppily: a span that swept up the sentence-final full stop, or a trailing
        // space, would otherwise leave an uncovered "sensitive" character that identifies nobody. With
        // thirty annotations per document that reports a leak in nearly every document, and a meter
        // that cries wolf is as useless as one that stays quiet.
        List<Detection> goldSpans = gold.Select(span => Tighten(span, original)).Where(span => span.Length > 0).ToList();
        List<Detection> foundSpans = found.ToList();
        List<(int Start, int End)> goldRanges = Union(goldSpans, original.Length);
        List<(int Start, int End)> foundRanges = Union(foundSpans, original.Length);

        int sensitive = goldRanges.Sum(range => range.End - range.Start);
        int covered = IntersectionLength(goldRanges, foundRanges);
        int removed = foundRanges.Sum(range => range.End - range.Start);

        // Survival is checked span by span, on the annotations as they were written — never on the
        // union. Two annotators marking the same passage at different granularities is the normal case
        // (Phase 1 requires two per document), and merging them first asks only whether the widest
        // reading survived. Gold "Sofía Reyes" and gold "Reyes", output "Ale Reyes": the merged question
        // is "is 'Sofía Reyes' still here", the answer is no, and "Reyes" goes unnoticed.
        int surviving = 0;
        var survivingByKind = new Dictionary<IdentifierKind, int>();
        foreach (Detection span in goldSpans)
        {
            // Ignoring case AND accents, through the one place that decides what "the same letters"
            // means. This used to compare ordinally, so "Sofía" surviving as "Sofia" read as a clean
            // transcript while "SOFÍA" was caught — the meter's private copy of the equality rule was
            // strict in the one direction that hides leaks, and the library's own detector could find
            // every identifier in a text this scored at recall 1.00, precision 1.00, no leak.
            if (Folding.Contains(redacted, span.TextIn(original)))
            {
                surviving++;
                survivingByKind[span.Kind] = survivingByKind.GetValueOrDefault(span.Kind) + 1;
            }
        }

        var byKind = new Dictionary<IdentifierKind, KindScore>();
        foreach (IdentifierKind kind in goldSpans.Select(s => s.Kind).Concat(foundSpans.Select(s => s.Kind)).Distinct())
        {
            List<(int Start, int End)> goldOfKind = Union(goldSpans.Where(s => s.Kind == kind), original.Length);
            List<(int Start, int End)> foundOfKind = Union(foundSpans.Where(s => s.Kind == kind), original.Length);
            int removedOfKind = foundOfKind.Sum(range => range.End - range.Start);

            byKind[kind] = new KindScore(
                Sensitive: goldOfKind.Sum(range => range.End - range.Start),
                Covered: IntersectionLength(goldOfKind, foundRanges),
                OverRedacted: removedOfKind - IntersectionLength(foundOfKind, goldRanges),
                SurvivingSpans: survivingByKind.GetValueOrDefault(kind));
        }

        var byDetector = new Dictionary<string, DetectorScore>(StringComparer.Ordinal);
        foreach (string detector in foundSpans.Select(s => s.DetectorId).Distinct(StringComparer.Ordinal))
        {
            List<(int Start, int End)> ofDetector = Union(foundSpans.Where(s => s.DetectorId == detector), original.Length);
            int coveredByDetector = IntersectionLength(ofDetector, goldRanges);
            byDetector[detector] = new DetectorScore(
                coveredByDetector,
                ofDetector.Sum(range => range.End - range.Start) - coveredByDetector);
        }

        return new DeidScore(sensitive, covered, removed - covered, surviving)
        {
            ByKind = byKind,
            ByDetector = byDetector,
        };
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
    /// Trims whitespace and edge punctuation off an annotation. An annotator's selection is a gesture at
    /// a value; the value is what is inside it.
    /// </summary>
    private static Detection Tighten(Detection span, string original)
    {
        int start = Math.Max(0, span.Start);
        int end = Math.Min(original.Length, span.End);

        while (start < end && !char.IsLetterOrDigit(original[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetterOrDigit(original[end - 1]))
        {
            end--;
        }

        return span with { Start = start, Length = end - start };
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
