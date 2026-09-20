using System.Text;

namespace Silueta.Core;

/// <summary>
/// A coarse spelling of a word as it sounds, shared by Spanish and English.
/// <para>
/// The input to this library is not writing: it is what a speech recogniser thought it heard. In one real
/// meeting transcript, "Navi" came out as "Na'vi", "AVI" and "novel", and one speaker's "shift" came out
/// as "chief" thirteen times. A redactor that looks for the name as the agency spells it misses every one
/// of those, and a missed name is a leak.
/// </para>
/// <para>
/// So names are compared by sound, not by letters. The rules below collapse the confusions that actually
/// happen between and within the two languages — b/v, s/z/c, y/j/ll, silent h, ph/f, qu/k — and leave
/// everything else alone. This is deliberately cruder than Double Metaphone: a coarse key with an edit
/// distance on top catches more ASR damage than a precise key that still demands the right consonant.
/// The cost is false positives, which is why <see cref="KnownValueDetector"/> only ever compares against
/// values the caller already knows, never against open text.
/// </para>
/// </summary>
public static class PhoneticKey
{
    /// <summary>Returns the phonetic key of a single word. Empty for anything with no letters.</summary>
    public static string Compute(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return string.Empty;
        }

        string flat = Folding.StripAccents(word).ToLowerInvariant();
        var sb = new StringBuilder(flat.Length);

        for (int i = 0; i < flat.Length; i++)
        {
            char c = flat[i];
            char next = i + 1 < flat.Length ? flat[i + 1] : '\0';

            if (!char.IsLetterOrDigit(c))
            {
                continue; // apostrophes and hyphens carry no sound
            }

            switch (c)
            {
                case 'h':
                    // Silent in Spanish, and the usual home of "Jose" vs "Hose". "ch" keeps its own sound.
                    if (i > 0 && flat[i - 1] == 'c')
                    {
                        sb.Append('h');
                    }

                    break;

                case 'v':
                    sb.Append('b');
                    break;

                case 'z':
                    sb.Append('s');
                    break;

                case 'c':
                    if (next is 'e' or 'i')
                    {
                        sb.Append('s');
                    }
                    else if (next == 'h')
                    {
                        sb.Append('c');
                    }
                    else
                    {
                        sb.Append('k');
                    }

                    break;

                case 'q':
                    sb.Append('k');
                    if (next == 'u')
                    {
                        i++; // "qu" is one sound
                    }

                    break;

                case 'k':
                    sb.Append('k');
                    break;

                case 'p':
                    if (next == 'h')
                    {
                        sb.Append('f');
                        i++; // Sophia and Sofia have to land on the same key
                    }
                    else
                    {
                        sb.Append('p');
                    }

                    break;

                case 'g':
                    sb.Append(next is 'e' or 'i' ? 'h' : 'g');
                    break;

                case 'j':
                    // Spanish jota and English j both end up here; so does the y of "Yamilet" below,
                    // because a recogniser hearing "Jamileth" and "Yamilet" is hearing one name.
                    sb.Append('y');
                    break;

                case 'l':
                    // Deliberately NOT mapping "ll" to the Spanish y-sound. Doing that buys Guillermo
                    // and costs Eleanor, and English doubled letters are far more common in this corpus
                    // than Spanish ll. The doubling is handled by Collapse, so Ellenor meets Eleanor.
                    sb.Append('l');
                    break;

                case 'y':
                    // Consonant before a vowel ("Yamilet"), vowel anywhere else ("Navy", "Mary").
                    sb.Append(IsVowel(next) ? 'y' : 'i');
                    break;

                case 'w':
                    sb.Append('b');
                    break;

                case 'x':
                    sb.Append(sb.Length == 0 ? "s" : "ks");
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        return Collapse(sb.ToString());
    }

    /// <summary>The key of a whole phrase, one word at a time, joined by spaces.</summary>
    public static string[] ComputeAll(IEnumerable<Token> tokens)
    {
        var keys = new List<string>();
        foreach (Token token in tokens)
        {
            keys.Add(Compute(token.Text));
        }

        return keys.ToArray();
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    /// <summary>
    /// Doubled letters are a spelling convention, not a sound: Ann and An, Sussan and Susan.
    /// <para>
    /// Digits are exempt, and the exemption is the whole point. A repeat in a word is a way of writing one
    /// sound; a repeat in a number is another figure. Collapsing it made the key of the account
    /// <c>1111</c> the single character <c>1</c>, and of <c>1122334455</c> the everyday <c>12345</c> — so a
    /// roster that knew an account number could quietly redact a dose, a room or an extension. There is no
    /// recogniser damage to absorb here either: a recogniser that mishears a digit writes a different
    /// number, not a similar-sounding one.
    /// </para>
    /// </summary>
    private static string Collapse(string s)
    {
        if (s.Length < 2)
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        char previous = '\0';
        foreach (char c in s)
        {
            if (c != previous || char.IsDigit(c))
            {
                sb.Append(c);
            }

            previous = c;
        }

        return sb.ToString();
    }

}
