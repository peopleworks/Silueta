using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Silueta.Core;

/// <summary>
/// The one case where Safe Harbor takes back the year it allows: a date of birth that puts someone over 89.
/// <para>
/// 45 CFR § 164.514(b)(2)(i)(C) keeps the year of a date, and then excepts "all ages over 89 and all elements
/// of dates (including year) indicative of such age", which may be kept only "aggregated into a single category
/// of age 90 or older". A birth year is such an element. The pattern pack found the age said as a number — "94
/// years old" — and nothing found the same fact said as a date, so "born in 1930" came back with 1930 in it
/// under a manifest that said safe-harbor.
/// </para>
/// <para>
/// This is not a rule about shapes, which is why it is not in the pack: whether 1930 is over 89 depends on when
/// you ask. The run supplies the reference — the record's own date when the caller gives one, the day of the run
/// otherwise — and the manifest records which, because the same transcript redacted in two different years is
/// two different corpora. The test is deliberately blunt: reference year minus birth year, 90 or more. A
/// birthday nobody stated falls either side of the run, and the side that removes is the side to fall on.
/// </para>
/// <para>
/// It finds the year by reading what the pack already found. A date the pack detected near a word about birth
/// is re-read as an age; a bare year — which no date rule matches, because a year alone is not a date — becomes
/// a span of its own. Either way one span is replaced, and the year does not survive inside it.
/// </para>
/// </summary>
public static partial class BirthYear
{
    /// <summary>Named and fingerprinted, like the kinship rule: a rule that decides what gets found belongs in
    /// the manifest.</summary>
    public const string RuleVersion = "birth-year/1";

    /// <summary>How far after a word about birth a date may sit and still be that birth's date.</summary>
    private const int Window = 40;

    /// <summary>The words that make a nearby date a date of birth, in both languages.</summary>
    public static readonly IReadOnlyList<string> Cues =
        ["born", "birthday", "birthdate", "birth", "dob", "nació", "nacio", "nacida", "nacido", "nacimiento"];

    [GeneratedRegex(@"\b(?:born|birthday|birthdate|birth|d\.?o\.?b|naci[oó]|nacida|nacido|nacimiento)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CuePattern();

    /// <summary>A four-digit year, the same shape the engine keeps when it keeps only a year.</summary>
    [GeneratedRegex(@"\b(1[5-9]\d{2}|20\d{2})\b")]
    internal static partial Regex YearPattern();

    /// <summary>A digest of the words this rule reads, so two corpora searched with different ones differ.</summary>
    public static string Fingerprint =>
        $"{RuleVersion} {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\t', Cues) + $"\t{Window}")))[..16]}";

    /// <summary>
    /// Rewrites, in place, every date near a word about birth whose year could make the person 90 — as an age
    /// over 89, which Safe Harbor generalises — and adds a span for a bare year no date rule matched.
    /// </summary>
    internal static void Reframe(string text, List<Detection> found, int referenceYear)
    {
        // Born in this year or earlier, the person may already have turned 90 by the reference date.
        int cutoff = referenceYear - 90;

        foreach (Match cue in CuePattern().Matches(text))
        {
            int from = cue.Index + cue.Length;
            if (from >= text.Length)
            {
                continue;
            }

            // Never across the end of a sentence or a line: "a birth defect. She moved in 1930" is not a date
            // of birth, and removing that year would be removing a year Safe Harbor allows.
            string window = text[from..Math.Min(text.Length, from + Window)];
            int stop = window.AsSpan().IndexOfAny('.', '\n', '\r');
            if (stop >= 0)
            {
                window = window[..stop];
            }

            if (YearPattern().Match(window) is not { Success: true } year ||
                int.Parse(year.Value, System.Globalization.CultureInfo.InvariantCulture) > cutoff)
            {
                continue;
            }

            int at = from + year.Index;
            int existing = found.FindIndex(d => d.Kind == IdentifierKind.Date && d.Start <= at && at < d.End);

            if (existing >= 0)
            {
                // The pack found the whole date; this says what it is. The span does not change — the year is
                // inside it — and the confidence goes above every date rule's, so that when the two readings of
                // one span meet, the one that removes the year is the one that speaks for it.
                found[existing] = found[existing] with
                {
                    Kind = IdentifierKind.AgeOver89,
                    DetectorId = RuleVersion,
                    Confidence = 0.99,
                };
            }
            else
            {
                found.Add(new Detection(
                    at, year.Length, IdentifierKind.AgeOver89, RuleVersion, 0.99, SubjectId: null, MatchKind.Pattern));
            }
        }
    }
}
