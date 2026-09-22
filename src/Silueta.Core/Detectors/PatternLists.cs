using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Silueta.Core;

/// <summary>
/// The word lists a pattern rule names instead of spelling out: <c>{{us-state}}</c> for every state's name, in
/// English and in Spanish, and <c>{{us-state-code}}</c> for every postal code of one.
/// <para>
/// Three rules need the states — the one that finds a state, the one that finds the city before a state, and the
/// one that finds the ZIP after one. Written out three times, a state added to one and not the others is a city
/// found in Arizona and missed in New Mexico, which is the second copy of a rule this library keeps finding in
/// itself. So the list is data, once, embedded beside the pack, and a rule refers to it by name. A lineage's own
/// rules can name the same lists.
/// </para>
/// <para>
/// The same holds for word lists — the street suffixes of USPS Publication 28, INEGI's types of road, the words
/// that open a sentence. Each sits under <c>words</c> in the file with the standard it was taken from beside it,
/// and a rule names it: <c>{{street-suffix-us}}</c>, <c>{{vialidad-mx}}</c>, <c>{{sentence-opener}}</c>.
/// </para>
/// </summary>
public static partial class PatternLists
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Alternations = new(Load);

    [GeneratedRegex(@"\{\{([a-z0-9-]+)\}\}")]
    private static partial Regex Placeholder();

    /// <summary>The names a rule can use between double braces.</summary>
    public static IReadOnlyCollection<string> Names => [.. Alternations.Value.Keys];

    /// <summary>A pattern with every list it names spelled out as an alternation. Throws when it names a list this
    /// build does not have; <see cref="PatternDetector"/> skips such a rule instead.</summary>
    public static string Expand(string pattern) =>
        TryExpand(pattern, out string expanded)
            ? expanded
            : throw new InvalidOperationException(
                $"The pattern names a list this build does not have. Lists: {string.Join(", ", Names)}.");

    internal static bool TryExpand(string pattern, out string expanded) => TryExpand(pattern, null, out expanded);

    /// <param name="extra">A lineage's own lists, which its rules may name beside the build's.</param>
    internal static bool TryExpand(
        string pattern, IReadOnlyDictionary<string, IReadOnlyList<string>>? extra, out string expanded)
    {
        bool known = true;
        expanded = Placeholder().Replace(pattern, match =>
        {
            string name = match.Groups[1].Value;
            if (Alternations.Value.TryGetValue(name, out string? alternation))
            {
                return alternation;
            }

            if (extra is not null && extra.TryGetValue(name, out IReadOnlyList<string>? words))
            {
                return Words(words);
            }

            known = false;
            return match.Value;
        });

        return known;
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        const string resource = "Silueta.Core.Detectors.Packs.lists.core.json";
        using Stream stream = typeof(PatternLists).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The pattern lists '{resource}' are not embedded in this build.");
        using JsonDocument document = JsonDocument.Parse(stream);

        var names = new List<string>();
        var codes = new List<string>();
        foreach (JsonElement state in document.RootElement.GetProperty("usStates").EnumerateArray())
        {
            codes.Add(state.GetProperty("code").GetString()!);
            names.AddRange(state.GetProperty("names").EnumerateArray().Select(name => name.GetString()!));
        }

        var lists = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["us-state"] = Words(names),
            ["us-state-code"] = Alternation(codes.Order(StringComparer.Ordinal)),
        };

        // The named word lists, each from a standard cited beside it in the file.
        foreach (JsonProperty list in document.RootElement.GetProperty("words").EnumerateObject())
        {
            lists.Add(list.Name, Words(list.Value.EnumerateArray().Select(word => word.GetString()!)));
        }

        return lists;
    }

    /// <summary>
    /// Longest first, so "West Virginia" is tried before "Virginia" and "Northeast" before "North" at the same
    /// position; words joined by any run of spaces, because a transcript's spacing is the recogniser's.
    /// </summary>
    private static string Words(IEnumerable<string> words) =>
        Alternation(words
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(word => word.Length)
            .ThenBy(word => word, StringComparer.Ordinal)
            .Select(word => string.Join(@"\s+", word.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))));

    private static string Alternation(IEnumerable<string> options) =>
        new StringBuilder("(?:").AppendJoin('|', options).Append(')').ToString();
}
