namespace Silueta.Core;

/// <summary>Edit distance, used on phonetic keys rather than on raw spelling.</summary>
public static class Similarity
{
    /// <summary>Levenshtein distance with two rolling rows: the transcripts are long, the words are short.</summary>
    public static int Distance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// Exact distance when it fits the budget, otherwise budget + 1. Only the band a successful edit
    /// path could visit is kept: a rejected word needs a verdict, not its full distance. The matcher
    /// allows at most two edits, so the rows occupy at most five integers each, even for a long token.
    /// </summary>
    internal static int DistanceWithin(string a, string b, int budget)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 0;
        }

        int outside = budget + 1;
        if (budget == 0 || Math.Abs(a.Length - b.Length) > budget)
        {
            return outside;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return Math.Max(a.Length, b.Length);
        }

        Span<int> previous = stackalloc int[2 * budget + 1];
        Span<int> current = stackalloc int[2 * budget + 1];
        int previousStart = 0;
        int previousEnd = Math.Min(b.Length, budget);
        for (int j = 0; j <= previousEnd; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            int start = Math.Max(0, i - budget);
            int end = Math.Min(b.Length, i + budget);
            int minimum = outside;
            for (int j = start; j <= end; j++)
            {
                int distance;
                if (j == 0)
                {
                    distance = i;
                }
                else
                {
                    int deletion = j >= previousStart && j <= previousEnd
                        ? previous[j - previousStart] + 1 : outside;
                    int insertion = j > start ? current[j - start - 1] + 1 : outside;
                    int substitution = j - 1 >= previousStart && j - 1 <= previousEnd
                        ? previous[j - 1 - previousStart] + (a[i - 1] == b[j - 1] ? 0 : 1) : outside;
                    distance = Math.Min(deletion, Math.Min(insertion, substitution));
                }

                current[j - start] = distance;
                minimum = Math.Min(minimum, distance);
            }

            if (minimum > budget)
            {
                return outside;
            }

            Span<int> swap = previous;
            previous = current;
            current = swap;
            previousStart = start;
            previousEnd = end;
        }

        return Math.Min(previous[b.Length - previousStart], outside);
    }

    /// <summary>1.0 for identical, 0.0 for nothing in common. Compared against the detector's threshold.</summary>
    public static double Ratio(string a, string b)
    {
        int longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 1.0 : 1.0 - ((double)Distance(a, b) / longest);
    }
}
