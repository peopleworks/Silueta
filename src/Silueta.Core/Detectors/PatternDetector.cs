using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Silueta.Core;

/// <summary>One rule of a pattern pack: a regular expression and what a match means.</summary>
public sealed class PatternRule
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Name of an <see cref="IdentifierKind"/>. A pack that names a kind we do not know is a
    /// pack written against a newer version, so the rule is skipped rather than crashing the run.</summary>
    public string Kind { get; set; } = nameof(IdentifierKind.Other);

    public string Regex { get; set; } = string.Empty;

    public double Confidence { get; set; } = 0.9;
}

/// <summary>
/// A pattern pack could not be run over this text.
/// <para>
/// Its own type, and deliberately bare. The framework's <see cref="RegexMatchTimeoutException"/> carries
/// the input that defeated the expression — in <c>Input</c> and inside <c>Message</c> — so a pack with
/// exponential backtracking turns any log that catches it into a copy of the transcript. This exception
/// names the rule and nothing else, and it is thrown without an inner exception on purpose: an inner one
/// would put the input straight back into <c>ToString()</c>.
/// </para>
/// </summary>
public sealed class PatternPackException : Exception
{
    public PatternPackException(string ruleId, string reason)
        : base($"Pattern rule '{ruleId}' could not be run: {reason}. The input is withheld deliberately.")
        => RuleId = ruleId;

    /// <summary>Which rule failed. Enough to fix the pack, and it identifies nobody.</summary>
    public string RuleId { get; }
}

/// <summary>
/// The identifiers that have a shape rather than a name: phone numbers, e-mail, record numbers, dates,
/// ages above 89. These need no roster and no model, and they are the part of Safe Harbor that can be
/// argued to a reviewer line by line.
/// </summary>
public sealed class PatternDetector : IDetector, IDetectorProvenance
{
    /// <summary>Long enough for any honest rule on a shift-length transcript, short enough that a bad one
    /// fails instead of hanging the run.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly List<(PatternRule Rule, Regex Regex, IdentifierKind Kind)> _rules = new();
    private readonly List<string> _skipped = new();

    /// <param name="timeout">Per-match ceiling. The text comes from outside, and so does the pack.</param>
    public PatternDetector(IEnumerable<PatternRule> rules, TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? DefaultTimeout;

        foreach (PatternRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Regex) || !IdentifierKindExtensions.TryParseName(rule.Kind, out IdentifierKind kind))
            {
                // A pack naming a kind we do not know is a pack written against a newer version, so the
                // rule is skipped rather than crashing the run — but it is written down, because silence
                // makes "found nothing" and "never ran" the same thing in the manifest.
                _skipped.Add(string.IsNullOrWhiteSpace(rule.Id) ? "(unnamed rule)" : rule.Id);
                continue;
            }

            _rules.Add((rule, new Regex(rule.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, limit), kind));
        }
    }

    public string Id => "pattern";

    public int RulesLoaded => _rules.Count;

    /// <summary>The kinds this pack has at least one rule for. What an evaluation counts as "in scope" comes
    /// from here rather than from a list someone wrote down, so a rule added to the pack widens the scope and
    /// a rule that exists but fails still counts against the matcher.</summary>
    public IReadOnlyCollection<IdentifierKind> Kinds => [.. _rules.Select(r => r.Kind).Distinct()];

    public IReadOnlyList<string> RulesSkipped => _skipped;

    /// <summary>
    /// A digest of the loaded rules: id, kind, pattern and confidence, sorted. Over the patterns
    /// themselves rather than the pack's file name, because a pack is a file anyone can edit.
    /// </summary>
    public string Fingerprint
    {
        get
        {
            var canonical = new StringBuilder("silueta-pack/1\n");
            foreach ((PatternRule rule, _, IdentifierKind kind) in _rules.OrderBy(r => r.Rule.Id, StringComparer.Ordinal))
            {
                canonical.Append(rule.Id).Append('\t')
                    .Append(kind).Append('\t')
                    .Append(rule.Regex).Append('\t')
                    .Append(rule.Confidence.ToString("R", CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
            return Convert.ToHexStringLower(digest.AsSpan(0, 8));
        }
    }

    /// <summary>Loads a pack embedded in this assembly. "core" is the one that ships.</summary>
    public static PatternDetector FromEmbeddedPack(string pack = "core")
    {
        string resource = $"Silueta.Core.Detectors.Packs.patterns.{pack}.json";
        using Stream? stream = typeof(PatternDetector).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Pattern pack '{pack}' is not embedded in this build.");

        PatternRule[] rules = JsonSerializer.Deserialize(stream, SiluetaJsonContext.Default.PatternRuleArray) ?? [];
        return new PatternDetector(rules);
    }

    public IEnumerable<Detection> Detect(string text, DeidentificationContext context)
    {
        var results = new List<Detection>();
        if (string.IsNullOrEmpty(text))
        {
            return results;
        }

        foreach ((PatternRule rule, Regex regex, IdentifierKind kind) in _rules)
        {
            // The timeout fires while the matches are being enumerated, not when Matches() is called,
            // so the whole walk sits inside the guard.
            try
            {
                foreach (Match match in regex.Matches(text))
                {
                    if (match.Length > 0)
                    {
                        results.Add(new Detection(
                            match.Index,
                            match.Length,
                            kind,
                            $"{Id}:{rule.Id}",
                            rule.Confidence,
                            SubjectId: null,
                            MatchKind.Pattern));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Caught and dropped on the floor: not rethrown, not wrapped, not logged. Everything
                // about that object except the fact that it happened is a copy of the transcript.
                throw new PatternPackException(rule.Id, "it exceeded its time limit on this input");
            }
        }

        return results;
    }
}
