using System.Globalization;
using System.Text;

namespace Silueta.Core;

/// <summary>
/// When two spellings count as the same letters, in one place.
/// <para>
/// <see cref="Detection"/> defines an exact match as "letter for letter, ignoring case and accents", and
/// <see cref="PhoneticKey"/> strips accents before it does anything else. The leak meter had its own,
/// stricter copy of that idea — an ordinal comparison — so <c>Sofía</c> surviving as <c>Sofia</c> read as
/// a clean transcript while <c>SOFÍA</c> was caught. Two copies of a rule are two rules, and the one that
/// drifts is always the one nobody is looking at.
/// </para>
/// </summary>
public static class Folding
{
    /// <summary>Removes the accents from a string, leaving the letters underneath.</summary>
    public static string StripAccents(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Whether two strings are the same letters, ignoring case and accents: "Zohó" and "ZOHO".</summary>
    public static bool SameLetters(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return string.Equals(StripAccents(a), StripAccents(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="needle"/> appears in <paramref name="haystack"/>, ignoring case and
    /// accents. Deliberately generous: this decides whether an identifier survived a redaction, and a
    /// false alarm costs someone a second look while the other kind of mistake costs a person.
    /// </summary>
    public static bool Contains(string haystack, string needle)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);

        return needle.Length != 0
            && StripAccents(haystack).Contains(StripAccents(needle), StringComparison.OrdinalIgnoreCase);
    }
}
