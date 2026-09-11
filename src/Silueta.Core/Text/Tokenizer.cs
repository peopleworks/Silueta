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
    /// </summary>
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool isWordChar = char.IsLetterOrDigit(c);

            if (!isWordChar && start >= 0 && IsInnerJoiner(c) && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]))
            {
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
        }

        if (start >= 0)
        {
            tokens.Add(new Token(start, text.Length - start, text[start..]));
        }

        return tokens;
    }

    private static bool IsInnerJoiner(char c) => c is '\'' or '’' or '-' or '‐';
}
