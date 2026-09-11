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

    /// <summary>1.0 for identical, 0.0 for nothing in common. Compared against the detector's threshold.</summary>
    public static double Ratio(string a, string b)
    {
        int longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 1.0 : 1.0 - ((double)Distance(a, b) / longest);
    }
}
