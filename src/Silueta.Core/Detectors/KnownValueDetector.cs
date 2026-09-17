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
            List<Token> valueTokens = Tokenizer.Tokenize(known.Value);
            string[] parts = PhoneticKey.ComputeAll(valueTokens);
            if (parts.Length > 0 && Array.TrueForAll(parts, static p => p.Length > 0))
            {
                // Where the roster value itself has sentence punctuation between two words — "St. Mary" —
                // the same punctuation in the transcript is part of the name, not the end of a sentence.
                bool[] punctuatedGaps = new bool[parts.Length - 1];
                for (int g = 0; g < punctuatedGaps.Length; g++)
                {
                    int from = valueTokens[g].End;
                    punctuatedGaps[g] = known.Value.AsSpan(from, valueTokens[g + 1].Start - from).IndexOfAny(SentenceEnds) >= 0;
                }

                targets.Add(new Target(known, parts, punctuatedGaps));
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

                if (CrossesABoundary(text, tokens, i, target))
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

                // Read locally to decide how the match happened; it does not travel on the detection.
                string matched = text[start..end];

                results.Add(new Detection(
                    start,
                    end - start,
                    target.Known.Kind,
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

    private static readonly System.Buffers.SearchValues<char> SentenceEnds = System.Buffers.SearchValues.Create(".!?…");

    /// <summary>
    /// Whether a window of adjacent words runs across the end of a sentence or a paragraph.
    /// <para>
    /// The window used to see only the words, so "We called Acme. Corporation tax is due" matched
    /// "Acme Corporation" and came back as one fused sentence with a person's name in it. A sentence end is
    /// a <c>. ! ? …</c> between two words of the window — unless the roster value has punctuation in the
    /// same place ("St. Mary Hospital"), or the word before it is a single letter ("John F. Kennedy").
    /// </para>
    /// <para>
    /// A blank line is a boundary; a single line break is <em>not</em>, and that asymmetry is deliberate.
    /// A transcript wrapped at a fixed width breaks lines wherever the column runs out, including inside a
    /// name. For a person that would cost little, because a person is also registered word by word. A
    /// company is registered only whole, so a wrap counted as a boundary would lose the company name
    /// entirely. A merged sentence is an ugly false positive; a lost company name is a leak.
    /// </para>
    /// </summary>
    private static bool CrossesABoundary(string text, List<Token> tokens, int offset, Target target)
    {
        for (int g = 0; g < target.Keys.Length - 1; g++)
        {
            Token before = tokens[offset + g];
            ReadOnlySpan<char> gap = text.AsSpan(before.End, tokens[offset + g + 1].Start - before.End);

            if (LineBreaks(gap) >= 2)
            {
                return true;
            }

            if (target.PunctuatedGaps[g])
            {
                continue;
            }

            int at = gap.IndexOfAny(SentenceEnds);
            if (at < 0)
            {
                continue;
            }

            bool initial = gap[at] == '.' && before.Text.Length == 1 && gap[(at + 1)..].IndexOfAny(SentenceEnds) < 0;
            if (!initial)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>"\r\n", "\n" and a lone "\r" each count once, so a Windows blank line is two breaks.</summary>
    private static int LineBreaks(ReadOnlySpan<char> gap)
    {
        int count = 0;
        for (int c = 0; c < gap.Length; c++)
        {
            if (gap[c] == '\n' || (gap[c] == '\r' && (c + 1 == gap.Length || gap[c + 1] != '\n')))
            {
                count++;
            }
        }

        return count;
    }

    private readonly record struct Target(KnownIdentifier Known, string[] Keys, bool[] PunctuatedGaps);
}
