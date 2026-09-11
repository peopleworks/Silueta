namespace Silueta.Core;

/// <summary>
/// Finds the identifiers the caller already knows, through whatever the speech recogniser did to them.
/// <para>
/// This is the detector that earns its keep. Open-ended name recognition on clinical text has two failure
/// modes that both hurt: it misses names it has never seen, and it deletes "Parkinson" because it looks
/// like a person. Matching against a known roster has neither. What it needs instead is tolerance for
/// spelling: the agency wrote "Sofía Reyes", the transcript says "Sophia Rays", and both have to land on
/// the same span.
/// </para>
/// <para>
/// Cost is words × known values. A four-hour shift is on the order of 40,000 words and a record has a few
/// dozen known values, so this is a few million key comparisons — milliseconds, and no model to load.
/// </para>
/// </summary>
public sealed class KnownValueDetector : IDetector
{
    private readonly double _threshold;
    private readonly int _minFuzzyLength;

    /// <param name="threshold">Minimum phonetic-key similarity, per word, for a fuzzy match. 0.84 accepts
    /// one edit in a six-letter key and two in a twelve-letter one.</param>
    /// <param name="minFuzzyLength">Below this key length only an exact key match counts. Short names are
    /// where fuzzy matching starts eating real words: "Ana" is two edits from "una".</param>
    public KnownValueDetector(double threshold = 0.84, int minFuzzyLength = 4)
    {
        _threshold = threshold;
        _minFuzzyLength = minFuzzyLength;
    }

    public string Id => "known-value";

    public IEnumerable<Detection> Detect(string text, DeidentificationContext context)
    {
        var results = new List<Detection>();
        if (string.IsNullOrEmpty(text) || context.Known.Count == 0)
        {
            return results;
        }

        List<Token> tokens = Tokenizer.Tokenize(text);
        string[] keys = PhoneticKey.ComputeAll(tokens);

        var targets = new List<Target>();
        foreach (KnownIdentifier known in context.Known)
        {
            string[] parts = PhoneticKey.ComputeAll(Tokenizer.Tokenize(known.Value));
            if (parts.Length > 0 && Array.TrueForAll(parts, static p => p.Length > 0))
            {
                targets.Add(new Target(known, parts));
            }
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            foreach (Target target in targets)
            {
                int words = target.Keys.Length;
                if (i + words > tokens.Count)
                {
                    continue;
                }

                double score = ScoreWindow(keys, i, target.Keys);
                if (score <= 0)
                {
                    continue;
                }

                int start = tokens[i].Start;
                int end = tokens[i + words - 1].End;
                string matched = text[start..end];

                results.Add(new Detection(
                    start,
                    end - start,
                    target.Known.Kind,
                    matched,
                    Id,
                    score,
                    target.Known.SubjectId,
                    Classify(matched, target.Known.Value, score)));
            }
        }

        return results;
    }

    /// <summary>The window's score is its weakest word: every part of a name has to be recognisable.</summary>
    private double ScoreWindow(string[] keys, int offset, string[] targetKeys)
    {
        double weakest = 1.0;

        for (int k = 0; k < targetKeys.Length; k++)
        {
            string found = keys[offset + k];
            string wanted = targetKeys[k];

            if (found.Length == 0)
            {
                return 0;
            }

            if (string.Equals(found, wanted, StringComparison.Ordinal))
            {
                continue;
            }

            if (wanted.Length < _minFuzzyLength || found.Length < _minFuzzyLength)
            {
                return 0;
            }

            double ratio = Similarity.Ratio(found, wanted);
            if (ratio < _threshold)
            {
                return 0;
            }

            weakest = Math.Min(weakest, ratio);
        }

        return weakest;
    }

    private static MatchKind Classify(string matched, string known, double score) => score >= 1.0
        ? string.Equals(matched, known, StringComparison.OrdinalIgnoreCase) ? MatchKind.Exact : MatchKind.Phonetic
        : MatchKind.Fuzzy;

    private readonly record struct Target(KnownIdentifier Known, string[] Keys);
}
