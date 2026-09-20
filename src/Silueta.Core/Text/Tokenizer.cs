using System.Globalization;
using System.Text;

namespace Silueta.Core;

/// <summary>A word with its position in the original text.</summary>
public readonly record struct Token(int Start, int Length, string Text)
{
    public int End => Start + Length;
}

/// <summary>
/// Splits text into words while keeping the offsets of the original string, because every replacement
/// later has to point back at exactly what it replaced.
/// </summary>
public static class Tokenizer
{
    /// <summary>
    /// Letters and digits form a word. An apostrophe or hyphen stays inside the word when it has a letter
    /// on both sides — that single rule is what turns the transcript's "Na'vi" into one token instead of
    /// two, and the phonetic key then strips it. Speech recognisers invent apostrophes constantly.
    /// <para>
    /// It walks runes, not UTF-16 code units, and counts a combining mark as part of the word it sits on.
    /// Both matter and both were wrong. "Sofía" can be written with one code point for í or with i plus a
    /// combining acute — a roster typed on one machine and a transcript produced on another routinely
    /// disagree about which — and <c>char.IsLetterOrDigit</c> is false for the mark, so the decomposed form
    /// tokenised as "Sof" and "a": two words, neither of them a name, and a name that cannot be found is a
    /// leak. A character outside the basic plane fared worse still: each half of the surrogate pair is not
    /// a letter either, so a word made of them produced no tokens at all, and a transcript in such a script
    /// was a transcript this library saw as empty.
    /// </para>
    /// <para>
    /// The input string is deliberately <b>not</b> normalised, here or anywhere upstream. Every offset in
    /// this library indexes the caller's own string — a detection, a gold span, the text handed back — and
    /// normalising would move all of them and hand back a transcript nobody asked to have rewritten. Making
    /// the split form-agnostic costs nothing and keeps that promise; the phonetic key strips accents from
    /// either form, so the two spellings meet there.
    /// </para>
    /// </summary>
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        int start = -1;
        int i = 0;
        while (i < text.Length)
        {
            // An unpaired surrogate is not a rune at all. It is treated as one code unit that is not a word
            // character, which ends the word it follows: damaged input should break a name apart, not the
            // loop that is reading it.
            bool whole = Rune.TryGetRuneAt(text, i, out Rune rune);
            int width = whole ? rune.Utf16SequenceLength : 1;

            bool isWordChar = whole && (Rune.IsLetterOrDigit(rune) || IsCombining(rune));

            if (!isWordChar && start >= 0 && whole && IsInnerJoiner(rune) && Follows(text, i + width))
            {
                i += width;
                continue;
            }

            if (isWordChar)
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                tokens.Add(new Token(start, i - start, text[start..i]));
                start = -1;
            }

            i += width;
        }

        if (start >= 0)
        {
            tokens.Add(new Token(start, text.Length - start, text[start..]));
        }

        return tokens;
    }

    /// <summary>Whether a letter or digit follows at <paramref name="index"/>, so a joiner has a word on
    /// both sides rather than only behind it.</summary>
    private static bool Follows(string text, int index) =>
        index < text.Length && Rune.TryGetRuneAt(text, index, out Rune next) && Rune.IsLetterOrDigit(next);

    /// <summary>
    /// A mark that modifies the letter before it: the acute of a decomposed í, the harakat of Arabic, the
    /// vowel signs of Devanagari. It has no sound of its own and no standing to end a word.
    /// </summary>
    private static bool IsCombining(Rune rune) => Rune.GetUnicodeCategory(rune)
        is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsInnerJoiner(Rune rune) => rune.Value is '\'' or '’' or '-' or '‐';
}
