using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Silueta.Core;

namespace Silueta.Mcp.Tools;

/// <summary>
/// Why the matcher did or did not match two names, and which pattern rules exist. Both are reference
/// tools: they touch no transcript, no roster and no vault, and they answer the question a reader
/// actually has when a name survives a redaction.
/// </summary>
[McpServerToolType]
public static class MatchingTools
{
    [McpServerTool(Name = "explain_name_match", ReadOnly = true),
     Description("""
        Explains whether Silueta would treat two spellings as the same name, and why — the phonetic key
        of each, the edit distance between the keys, and the budget of edits that distance was allowed
        to spend.

        Use it when a name survived a redaction and you need to say what happened, or when adding
        someone to a roster and you want to know which recogniser mangles it will catch. Pass NAMES, not
        transcripts: two words, nothing identifying beyond what you already typed.

        Worked example: "Reyes" and "Rays" are the same surname, one as the agency writes it and one as
        the recogniser heard it. Their keys are "reyes" and "rais" — three edits apart, where a
        five-character key may spend one. Silueta misses it, and that is a matcher failure rather than a
        design decision.

        Pass the kind of the first spelling. The answer depends on it: a company or product whose key is
        shorter than four characters must also be the same letters, because "Inc" and "ink" share a key and
        a roster entry for "Inc" would otherwise redact every "ink". For a person the same pair matches.
        The verdict comes from the detector a redaction runs, not from a copy of its rules.
        """)]
    public static MatchExplanation ExplainNameMatch(
        [Description("One spelling, e.g. the name as the roster has it: \"Sofía Reyes\".")] string a,
        [Description("The other, e.g. as the recogniser wrote it: \"Sophia Rays\".")] string b,
        [Description("What kind of identifier the first spelling is: \"PatientName\", \"Organization\", \"Product\"… A company or a product is held to a stricter rule for short words than a person. Default OtherName.")] string kind = "OtherName")
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (!IdentifierKindExtensions.TryParseName(kind, out IdentifierKind parsedKind))
        {
            string hint = IdentifierKindExtensions.ClosestName(kind) is { } closest ? $" The closest kind is {closest}." : string.Empty;
            throw new McpException(
                $"That kind is not one this build knows.{hint} Kinds: {string.Join(", ", Enum.GetNames<IdentifierKind>())}.");
        }

        MatchTolerance tolerance = MatchTolerance.Default;
        bool person = parsedKind.IsPersonName();
        List<Token> left = Tokenizer.Tokenize(a);
        List<Token> right = Tokenizer.Tokenize(b);

        var words = new List<WordComparison>();
        for (int i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            string wordA = i < left.Count ? left[i].Text : string.Empty;
            string wordB = i < right.Count ? right[i].Text : string.Empty;
            string keyA = PhoneticKey.Compute(wordA);
            string keyB = PhoneticKey.Compute(wordB);

            string verdict;
            if (keyA.Length == 0 || keyB.Length == 0)
            {
                verdict = "no key — one side has no letters, or the names have a different number of words";
            }
            else if (string.Equals(keyA, keyB, StringComparison.Ordinal)
                && !person && keyA.Length < tolerance.ExactBelow && !Folding.SameLetters(wordA, wordB))
            {
                verdict = $"same key, but for a company or product a key shorter than {tolerance.ExactBelow} must also be " +
                    "the same letters — otherwise \"Inc\" would redact every \"ink\" — so this is a miss";
            }
            else if (string.Equals(keyA, keyB, StringComparison.Ordinal))
            {
                verdict = "same key: heard as the same word";
            }
            else
            {
                // The numbers are the ones the tolerance decided on, not a second calculation of them.
                bool close = tolerance.Accepts(keyA, keyB, out int edits, out int budget);
                verdict = budget == 0
                    ? $"a key of {Math.Min(keyA.Length, keyB.Length)} characters may spend no edits, and these are {edits} apart: a miss"
                    : close
                        ? $"close enough: {edits} edit(s) of a budget of {budget}"
                        : $"too far: {edits} edit(s), and the budget is {budget}";
            }

            words.Add(new WordComparison(
                wordA,
                wordB,
                keyA,
                keyB,
                keyA.Length == 0 || keyB.Length == 0 ? 0 : Similarity.Distance(keyA, keyB),
                keyA.Length == 0 || keyB.Length == 0 ? 0 : Math.Round(Similarity.Ratio(keyA, keyB), 3),
                verdict));
        }

        // The verdict is the detector's, not a re-derivation of it. This tool used to recompute the match
        // from the keys, which is a second copy of the matcher — and the first change to the real one (short
        // company names must be the same letters) left the copy saying "Inc" and "ink" match. The words
        // above explain; this decides, and it cannot drift from what a redaction would do.
        var context = new DeidentificationContext("explain").AddValue(a, parsedKind, "explain-subject");
        bool wouldMatch = right.Count > 0 && new KnownValueDetector(tolerance)
            .Detect(b, context)
            .Any(d => d.Start == right[0].Start && d.End == right[^1].End);

        return new MatchExplanation(
            a,
            b,
            wouldMatch,
            Math.Round(words.Count == 0 ? 0 : words.Min(w => w.Ratio), 3),
            tolerance.ToString(),
            words,
            "A name is matched word by word and scored by its weakest word: every part has to be " +
            "recognisable. Two keys are one word when they are within a budget of edits read from the " +
            "shorter of them, rather than within a proportion of their length — a recogniser writes a " +
            "wrong letter, not a wrong percentage. The key is deliberately coarser than a principled " +
            "phonetic algorithm, because ASR damage is not phonetically principled: it substitutes whole " +
            "words.");
    }

    [McpServerTool(Name = "list_pattern_rules", ReadOnly = true),
     Description("""
        Lists the pattern rules that find identifiers by shape rather than by name — phone numbers,
        e-mail, URLs, IP addresses, record numbers, dates in English and Spanish, ages over 89 — with
        each rule's regular expression, the kind of identifier it emits and its confidence.

        Read it to see what a run will catch without a roster, and, more usefully, what it will not.
        HIPAA Safe Harbor names eighteen identifiers; this pack does not yet emit a postal code or a
        street address, and it recognises no number or date spoken as words.
        """)]
    public static PatternCatalog ListPatternRules(
        [Description("Filter by identifier kind, e.g. \"Date\" or \"Phone\". Empty = all.")] string kind = "")
    {
        List<PatternRuleInfo> rules = PatternPack.Rules
            .Where(rule => kind.Length == 0 || rule.Kind.Equals(kind.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(rule => new PatternRuleInfo(rule.Id, rule.Kind, rule.Regex, rule.Confidence))
            .ToList();

        string[] emitted = PatternPack.Rules.Select(rule => rule.Kind).Distinct().ToArray();
        string[] missing = Enum.GetNames<IdentifierKind>()
            .Where(name => !emitted.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Where(name => name is not ("PatientName" or "FamilyName" or "StaffName" or "OtherName" or "Other"))
            .ToArray();

        return new PatternCatalog(
            rules.Count,
            rules,
            missing,
            "Kinds listed under notCoveredByAnyRule have a policy action but no rule that emits them, so " +
            "nothing will ever be found for them. Names are not in that list because names come from the " +
            "roster, not from a pattern.");
    }
}

/// <summary>The core pack, read once. Reading it through the detector's own loader keeps this in step
/// with what actually runs rather than with a copy of the file.</summary>
internal static class PatternPack
{
    internal static readonly IReadOnlyList<PatternRule> Rules = Load();

    private static PatternRule[] Load()
    {
        using Stream? stream = typeof(PatternDetector).Assembly
            .GetManifestResourceStream("Silueta.Core.Detectors.Packs.patterns.core.json");

        return stream is null
            ? []
            : System.Text.Json.JsonSerializer.Deserialize(stream, SiluetaJsonContext.Default.PatternRuleArray) ?? [];
    }
}

public sealed record WordComparison(
    string WordA,
    string WordB,
    string KeyA,
    string KeyB,
    int EditDistance,
    double Ratio,
    string Verdict);

public sealed record MatchExplanation(
    string A,
    string B,
    bool WouldMatch,
    double WeakestWordRatio,

    /// <summary>The rule the two spellings were held to, in words.</summary>
    string Tolerance,
    IReadOnlyList<WordComparison> Words,
    string HowItWorks);

public sealed record PatternRuleInfo(string Id, string Kind, string Regex, double Confidence);

public sealed record PatternCatalog(
    int Count,
    IReadOnlyList<PatternRuleInfo> Rules,
    IReadOnlyList<string> NotCoveredByAnyRule,
    string Note);
