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
/// The identifiers that have a shape rather than a name: phone numbers, e-mail, record numbers, dates,
/// ages above 89. These need no roster and no model, and they are the part of Safe Harbor that can be
/// argued to a reviewer line by line.
/// </summary>
public sealed class PatternDetector : IDetector
{
    private readonly List<(PatternRule Rule, Regex Regex, IdentifierKind Kind)> _rules = new();

    public PatternDetector(IEnumerable<PatternRule> rules)
    {
        foreach (PatternRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Regex) || !Enum.TryParse(rule.Kind, ignoreCase: true, out IdentifierKind kind))
            {
                continue;
            }

            // A timeout, because the text comes from outside and a pack is a file anyone can send.
            var regex = new Regex(rule.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            _rules.Add((rule, regex, kind));
        }
    }

    public string Id => "pattern";

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
            foreach (Match match in regex.Matches(text))
            {
                if (match.Length > 0)
                {
                    results.Add(new Detection(
                        match.Index,
                        match.Length,
                        kind,
                        match.Value,
                        $"{Id}:{rule.Id}",
                        rule.Confidence,
                        SubjectId: null,
                        MatchKind.Pattern));
                }
            }
        }

        return results;
    }
}
