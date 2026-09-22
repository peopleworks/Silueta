using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Silueta.Core;

/// <summary>
/// A number dictated digit by digit — "five five five, oh one four seven" — found and named by what introduced it.
/// <para>
/// A recogniser writes what it heard, and a rule that reads <c>555-0147</c> reads nothing in "five five five".
/// This is the risky half of what is said out loud, and the risk is in the same transcript: a nurse reads a blood
/// pressure aloud too. Three things keep the two apart.
/// </para>
/// <list type="bullet">
/// <item><b>Single digits only.</b> "One thirty eight over eighty two" has tens in it, and "thirty" is not a digit,
/// so it never forms a run. Clinical figures are said in tens; identifiers are dictated a digit at a time.</item>
/// <item><b>A word that says what the number is, or a length that does.</b> After "call", "record", "member ID"
/// or "zip code", four digits are enough and the word names the kind. With no such word, a run as long as a
/// telephone number is one, and any other run of six or more is an identifier of no named kind — labelled, and
/// counted as <see cref="IdentifierKind.Other"/>.</item>
/// <item><b>A count is not a number.</b> "Nine eight seven six five four" is somebody counting, which in home care
/// is a cognitive test; a run holding five digits in a row that each go up or down by one is left alone unless a
/// word before it says it is a number. A full stop ends a run, so a count cannot take the first word of the next
/// sentence with it.</item>
/// </list>
/// <para>
/// Not a pack rule, for the same reason <see cref="BirthYear"/> is not: the run has to be read back as digits to
/// tell a count from a number and to hand a ZIP to the census, and a regular expression cannot read. The words
/// it reads are data — the digits with their values and the words that introduce a number live in
/// <c>lists.core.json</c> — and a pattern rule can name the same list, <c>{{digit-word}}</c>.
/// </para>
/// </summary>
public static partial class SpokenDigits
{
    /// <summary>Named and fingerprinted, like every rule that decides what gets found outside the pack. "/2"
    /// since a full stop ends a run and a count is recognised by a stretch: "/1", in 0.3.0-preview.2, read
    /// "…three two one. Dos o tres" as a telephone number.</summary>
    public const string RuleVersion = "spoken-digits/2";

    /// <summary>How far before a run a word may sit and still be the word that introduced it.</summary>
    private const int Window = 32;

    /// <summary>Without a word before it, a run shorter than this is left alone: a room, a dose, a score.</summary>
    private const int UnintroducedMinimum = 6;

    /// <summary>With a word before it, the word says it is a number, and four digits are enough.</summary>
    private const int IntroducedMinimum = 4;

    private static readonly Lazy<(Regex Run, Regex Separator, (Regex Cue, IdentifierKind Kind)[] Cues, string Fingerprint)> Rules =
        new(Build);

    /// <summary>The words a digit is said with, in both languages.</summary>
    public static IReadOnlyList<string> WordsFor(int digit) =>
        [.. PatternLists.DigitValues.Where(pair => pair.Value == digit).Select(pair => pair.Key).Order(StringComparer.Ordinal)];

    /// <summary>A digest of the words this rule reads and the lengths it counts, for the manifest.</summary>
    public static string Fingerprint => Rules.Value.Fingerprint;

    /// <summary>
    /// A run of digit words read back as digits — "six oh two" is <c>602</c> — or null when the text is not only
    /// digit words and the spaces, commas and dashes between them.
    /// </summary>
    public static string? ToDigits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var digits = new StringBuilder();
        foreach (string word in Rules.Value.Separator.Split(text.Trim()))
        {
            if (word.Length == 0)
            {
                continue;
            }

            if (!PatternLists.DigitValues.TryGetValue(word, out int value))
            {
                return null;
            }

            digits.Append((char)('0' + value));
        }

        return digits.Length == 0 ? null : digits.ToString();
    }

    /// <summary>Adds a span for every dictated number in the text that the rules above call one.</summary>
    internal static void Find(string text, List<Detection> found)
    {
        foreach (Match run in Rules.Value.Run.Matches(text))
        {
            (int start, string words) = WithoutALeadingOh(run);
            if (ToDigits(words) is not { } digits)
            {
                continue;
            }

            IdentifierKind? introduced = IntroducedAs(text, start);
            IdentifierKind? kind = introduced ?? ByLength(digits);

            if (kind is null ||
                digits.Length < (introduced is null ? UnintroducedMinimum : IntroducedMinimum) ||
                (introduced is null && IsACount(digits)))
            {
                continue;
            }

            found.Add(new Detection(
                start, run.Index + run.Length - start, kind.Value, RuleVersion,
                introduced is null ? 0.8 : 0.9, SubjectId: null, MatchKind.Pattern));
        }
    }

    /// <summary>"Oh, five five five…" is somebody saying oh. Dropped when a comma follows it, so it neither joins
    /// the number nor changes its length.</summary>
    private static (int Start, string Words) WithoutALeadingOh(Match run)
    {
        Match oh = LeadingOh().Match(run.Value);
        return oh.Success
            ? (run.Index + oh.Length, run.Value[oh.Length..])
            : (run.Index, run.Value);
    }

    /// <summary>The kind the words just before a run give it, reading the most specific first — "record number"
    /// is a record, not a phone — and never across the end of a sentence.</summary>
    private static IdentifierKind? IntroducedAs(string text, int start)
    {
        int from = Math.Max(0, start - Window);
        string before = text[from..start];
        int stop = before.LastIndexOfAny(['.', '!', '?', '\n', '\r']);
        if (stop >= 0)
        {
            before = before[(stop + 1)..];
        }

        foreach ((Regex cue, IdentifierKind kind) in Rules.Value.Cues)
        {
            if (cue.IsMatch(before))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>Seven, ten, or eleven starting with a one: the lengths of a North American telephone number.
    /// Anything else long enough is an identifier whose kind nothing says.</summary>
    private static IdentifierKind ByLength(string digits) =>
        digits.Length is 7 or 10 || (digits.Length == 11 && digits[0] == '1')
            ? IdentifierKind.Phone
            : IdentifierKind.Other;

    /// <summary>A stretch this long that only goes up or down by one is somebody counting.</summary>
    private const int CountingStretch = 5;

    /// <summary>
    /// Counting, not dictating: somewhere in the run, five digits or more in a row that each go up by one, or
    /// each go down by one.
    /// <para>
    /// A stretch rather than the whole run, because the whole run was the published defect: "…three two one.
    /// Dos o tres veces" took the "Dos" that followed, and one digit more was enough to make a count read as a
    /// telephone number. The cost is on the other side and it is accepted on purpose: a number nobody introduced
    /// whose digits climb for five in a row reads as a count and stays. Introduced — "call her at" — it never
    /// reaches this test.
    /// </para>
    /// </summary>
    private static bool IsACount(string digits)
    {
        int up = 1, down = 1;
        for (int i = 1; i < digits.Length; i++)
        {
            up = digits[i] - digits[i - 1] == 1 ? up + 1 : 1;
            down = digits[i - 1] - digits[i] == 1 ? down + 1 : 1;

            if (up >= CountingStretch || down >= CountingStretch)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^oh[ \t]*,[ \t]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingOh();

    private static (Regex, Regex, (Regex, IdentifierKind)[], string) Build()
    {
        // A comma or a dash between digits, never a full stop: a full stop between two digit words is the end of
        // one sentence and the start of the next, and a run that crossed it took "Dos" from "Dos o tres veces".
        const string separator = @"[ \t]*[,\-–][ \t]*|[ \t]+";
        string word = PatternLists.Expand("{{digit-word}}");

        // Compiled: it is built once per process and runs over every transcript, and it is the one rule here that
        // reads the whole text rather than a window of it.
        var run = new Regex(
            $@"\b{word}(?:(?:{separator}){word})+\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            PatternDetector.DefaultTimeout);

        // Most specific first: a record or an account is also "a number", and a phone cue would claim it.
        (string List, IdentifierKind Kind)[] order =
        [
            ("cue-record", IdentifierKind.RecordNumber),
            ("cue-account", IdentifierKind.AccountNumber),
            ("cue-zip", IdentifierKind.PostalCode),
            ("cue-phone", IdentifierKind.Phone),
        ];

        (Regex, IdentifierKind)[] cues =
        [
            .. order.Select(entry => (
                new Regex($@"\b{PatternLists.Expand("{{" + entry.List + "}}")}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternDetector.DefaultTimeout),
                entry.Kind)),
        ];

        var canonical = new StringBuilder(RuleVersion).Append('\n')
            .Append($"window {Window}, unintroduced {UnintroducedMinimum}, introduced {IntroducedMinimum}, counting {CountingStretch}\n")
            .Append("separator ").Append(separator).Append('\n');
        foreach ((string digit, int value) in PatternLists.DigitValues.OrderBy(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            canonical.Append(value).Append('=').Append(digit).Append('\n');
        }

        foreach ((string list, IdentifierKind kind) in order)
        {
            canonical.Append(kind).Append(':').AppendJoin(',', PatternLists.WordsOf(list)).Append('\n');
        }

        string fingerprint =
            $"{RuleVersion} {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))[..16]}";

        return (run, new Regex(separator, RegexOptions.CultureInvariant), cues, fingerprint);
    }
}
