using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Silueta.Core;

/// <summary>
/// Which three-digit ZIP prefixes Safe Harbor lets a record keep, and the census count that decides it.
/// <para>
/// 45 CFR § 164.514(b)(2)(i)(B): the first three digits of a ZIP code may stay where the area formed by every
/// ZIP sharing them holds more than 20,000 people "according to the current publicly available data from the
/// Bureau of the Census"; everywhere else they become 000. The table is the 2020 Decennial Census — total
/// population of every ZIP Code Tabulation Area, summed by prefix — derived by <c>tools/census/zip3.py</c> and
/// embedded with the source it came from, the day it was fetched and the digest of what was served.
/// </para>
/// <para>
/// Not HHS's list of seventeen. That list is the 2000 count, and the guidance that prints it (26 November 2012,
/// § 3.1) says not to rely on it "if more current data has been published". Against 2020 it keeps six prefixes
/// that must be zeroed — 202, 204, 205, 369, 753, 772 — and zeroes five that may be kept.
/// </para>
/// <para>
/// An allow-list, not a list of small prefixes. A prefix with no tabulation area at all — military mail,
/// unassigned ranges, ZIPs that are only post-office boxes — has nobody the census counted, so nothing says it
/// holds more than 20,000 people, and it is zeroed. A deny-list would have let every one of them through for the
/// reason that nobody wrote it down, which is the harmless default this library keeps finding in itself.
/// </para>
/// <para>
/// Compiled in, like Safe Harbor itself, and not something a lineage can replace: a file that could rewrite the
/// table could keep every prefix under a manifest that says safe-harbor. A new census is a new build.
/// </para>
/// </summary>
public static class CensusZipTable
{
    private static readonly Lazy<Loaded> Data = new(Load);

    /// <summary>The table's name, which goes into every manifest beside its <see cref="Fingerprint"/>.</summary>
    public static string Table => Data.Value.Table;

    /// <summary>More people than this, and the prefix stays.</summary>
    public static int Threshold => Data.Value.Threshold;

    /// <summary>The day the census data was fetched, ISO 8601.</summary>
    public static string Retrieved => Data.Value.Retrieved;

    /// <summary>Where the numbers came from.</summary>
    public static string Source => Data.Value.Source;

    /// <summary>The SHA-256 of the response the Census Bureau served, so a reader can tell whether it has changed.</summary>
    public static string SourceSha256 => Data.Value.SourceSha256;

    /// <summary>A digest of the threshold and every prefix's population.</summary>
    public static string Fingerprint => Data.Value.Fingerprint;

    /// <summary>Every prefix the census has at least one tabulation area for, in order. Any other prefix is zeroed.</summary>
    public static IReadOnlyList<string> Prefixes => Data.Value.Prefixes;

    /// <summary>The 2020 population behind a three-digit prefix, or null when the census has no area for it.</summary>
    public static int? PopulationOf(string prefix) =>
        Data.Value.Populations.TryGetValue(prefix, out int people) ? people : null;

    /// <summary>Whether Safe Harbor lets a record keep this prefix.</summary>
    public static bool MayKeep(string prefix) => PopulationOf(prefix) > Threshold;

    /// <summary>
    /// A written postal code widened the way Safe Harbor allows: <c>85004-1234</c> becomes <c>850XX</c>, and a
    /// code whose prefix is too small, or unknown to the census, becomes <c>000XX</c>. Said out loud — "eight five
    /// zero zero four" — it is read as the same digits and widened the same way. Null when what was written is not
    /// a five- or nine-digit code, because three digits of some other number are not an area.
    /// </summary>
    public static string? Generalize(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        string digits = string.Concat(written.Where(char.IsAsciiDigit));
        if (digits.Length == 0)
        {
            digits = SpokenDigits.ToDigits(written) ?? string.Empty;
        }

        if (digits.Length is not (5 or 9))
        {
            return null;
        }

        string prefix = digits[..3];
        return (MayKeep(prefix) ? prefix : "000") + "XX";
    }

    private sealed record Loaded(
        string Table,
        int Threshold,
        string Retrieved,
        string Source,
        string SourceSha256,
        string Fingerprint,
        IReadOnlyList<string> Prefixes,
        FrozenDictionary<string, int> Populations);

    private static Loaded Load()
    {
        const string resource = "Silueta.Core.Transform.Tables.zip3.census-2020.json";
        using Stream stream = typeof(CensusZipTable).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The census ZIP table '{resource}' is not embedded in this build.");
        using JsonDocument document = JsonDocument.Parse(stream);

        JsonElement root = document.RootElement;
        JsonElement source = root.GetProperty("source");
        int threshold = root.GetProperty("threshold").GetInt32();

        var populations = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonProperty entry in root.GetProperty("populations").EnumerateObject())
        {
            populations.Add(entry.Name, entry.Value.GetInt32());
        }

        var canonical = new StringBuilder();
        canonical.Append("threshold\t").Append(threshold.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach ((string prefix, int people) in populations)
        {
            canonical.Append(prefix).Append('\t').Append(people.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return new Loaded(
            root.GetProperty("table").GetString()!,
            threshold,
            source.GetProperty("retrieved").GetString()!,
            source.GetProperty("url").GetString()!,
            source.GetProperty("sha256").GetString()!,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())).AsSpan(0, 8)),
            [.. populations.Keys],
            populations.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
