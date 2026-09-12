using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Silueta.Core;

/// <summary>
/// What an organisation brings of its own: the word lists a surrogate is drawn from, the text that
/// replaces each kind of identifier, the pattern rules, and the language it all belongs to.
/// <para>
/// A clinic, a call centre and a law firm do not redact the same things, do not speak the same language
/// and have no reason to share one vendor's word lists. Until this type existed the pools were two
/// arrays in the source and the labels were a <c>switch</c>, so "bring your own" meant "fork the
/// library". The default lineage is now a JSON file embedded in the build rather than code, which is the
/// only way the sentence "the dictionaries do not live in the code" is literally true.
/// </para>
/// <para>
/// <b>What <see cref="Language"/> does and does not do.</b> It is recorded, it goes into the fingerprint
/// and it goes into the manifest. It does <em>not</em> select phonetic rules: the matcher's key is one
/// coarse Spanish-and-English key, compiled in, and a lineage saying <c>de-DE</c> gets exactly the same
/// matching as one saying <c>es-MX</c>. Saying so here because a metadata field that implies behaviour
/// it does not have is a lie with a schema.
/// </para>
/// </summary>
public sealed class SiluetaLineage
{
    private static readonly Lazy<SiluetaLineage> Builtin = new(LoadBuiltin);

    private SiluetaLineage(
        string name,
        string version,
        string language,
        SurrogatePools pools,
        IReadOnlyDictionary<IdentifierKind, string> labels,
        IReadOnlyDictionary<IdentifierKind, string> generalizations,
        IReadOnlyList<PatternRule> patterns,
        IReadOnlyList<string> skipped)
    {
        Name = name;
        Version = version;
        Language = language;
        Pools = pools;
        Labels = labels;
        Generalizations = generalizations;
        Patterns = patterns;
        Skipped = skipped;
        Fingerprint = ComputeFingerprint();
    }

    /// <summary>The lineage that ships with the library: the pools and labels this project has always
    /// had, as data rather than as code.</summary>
    public static SiluetaLineage Default => Builtin.Value;

    /// <summary>Identity. Goes in the manifest, so a corpus says which lineage produced it.</summary>
    public string Name { get; }

    public string Version { get; }

    /// <summary>Metadata and nothing more today — see the note on this class.</summary>
    public string Language { get; }

    public SurrogatePools Pools { get; }

    /// <summary>What replaces a kind that is labelled away: <c>[PHONE]</c>, or <c>[TELÉFONO]</c>.</summary>
    public IReadOnlyDictionary<IdentifierKind, string> Labels { get; }

    /// <summary>What replaces a kind that is widened rather than removed: "90 or older".</summary>
    public IReadOnlyDictionary<IdentifierKind, string> Generalizations { get; }

    /// <summary>The pattern rules this lineage brings. Empty means the pack compiled into the build.
    /// A lineage that brings rules <em>replaces</em> that pack rather than adding to it: two sources for
    /// one rule is two rules that will disagree.</summary>
    public IReadOnlyList<PatternRule> Patterns { get; }

    /// <summary>Keys naming a kind this build does not know, kept so "no such kind" and "nothing to
    /// replace" are not the same silence.</summary>
    public IReadOnlyList<string> Skipped { get; }

    /// <summary>
    /// A digest of the content, not of the file name and not of <see cref="Version"/>. Somebody who edits
    /// a pool and forgets to bump the version still produces a corpus that can be told apart from the
    /// one before it.
    /// </summary>
    public string Fingerprint { get; }

    public static SiluetaLineage FromJson(string json)
    {
        LineageFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, SiluetaJsonContext.Default.LineageFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"This is not a lineage file: {ex.Message}", ex);
        }

        if (file is null || string.IsNullOrWhiteSpace(file.Lineage) || string.IsNullOrWhiteSpace(file.Version))
        {
            // The vault taught this one: a missing field that defaults to something valid turns every
            // JSON object in the world into an empty-but-legal file of this type. A lineage with no name
            // and no version is somebody else's file being read by mistake.
            throw new InvalidOperationException(
                "This is not a lineage file: both \"lineage\" and \"version\" are required.");
        }

        IReadOnlyList<string> given = Clean(file.Pools?.Given, "pools.given");
        IReadOnlyList<string> family = Clean(file.Pools?.Family, "pools.family");

        var skipped = new List<string>();
        return new SiluetaLineage(
            file.Lineage.Trim(),
            file.Version.Trim(),
            string.IsNullOrWhiteSpace(file.Language) ? "und" : file.Language.Trim(),
            new SurrogatePools(given, family),
            Parse(file.Labels, skipped),
            Parse(file.Generalizations, skipped),
            file.Patterns ?? [],
            skipped);
    }

    /// <summary>Reads a lineage from disk. The file is the organisation's, so everything about it is
    /// checked rather than assumed.</summary>
    public static SiluetaLineage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromJson(File.ReadAllText(path));
    }

    /// <summary>The detectors this lineage asks for: its own pattern rules, or the built-in pack.</summary>
    public PatternDetector CreatePatternDetector() =>
        Patterns.Count > 0 ? new PatternDetector(Patterns) : PatternDetector.FromEmbeddedPack();

    /// <summary>What this lineage puts in place of a kind that is labelled away.</summary>
    public string LabelFor(IdentifierKind kind) =>
        Labels.TryGetValue(kind, out string? label) ? label : "[REMOVED]";

    /// <summary>What this lineage puts in place of a kind that is widened. Falls through to the label:
    /// a kind with no wider form still has to be removed, not kept.</summary>
    public string GeneralizationFor(IdentifierKind kind) =>
        Generalizations.TryGetValue(kind, out string? wider) ? wider : LabelFor(kind);

    private static IReadOnlyDictionary<IdentifierKind, string> Parse(
        Dictionary<string, string>? entries, List<string> skipped)
    {
        if (entries is null || entries.Count == 0)
        {
            return FrozenDictionary<IdentifierKind, string>.Empty;
        }

        var parsed = new Dictionary<IdentifierKind, string>();
        foreach ((string key, string value) in entries)
        {
            if (!Enum.TryParse(key, ignoreCase: true, out IdentifierKind kind))
            {
                // A lineage written against a newer build names kinds this one has never heard of. It is
                // skipped rather than fatal, and written down rather than swallowed.
                skipped.Add(key);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                parsed[kind] = value;
            }
        }

        return parsed.ToFrozenDictionary();
    }

    /// <summary>
    /// Every rule a pool has to obey, checked at load rather than discovered in a corpus.
    /// <para>
    /// The phonetic check is the one worth explaining. Two entries that sound alike are two surrogates
    /// the matcher cannot tell apart, so a later pass over the redacted text reads them as one person —
    /// which merges two people in the corpus and cannot be undone from the vault, because the vault
    /// recorded two distinct names.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> Clean(List<string>? entries, string field)
    {
        if (entries is null || entries.Count == 0)
        {
            throw new InvalidOperationException($"A lineage needs {field}: a surrogate has to come from somewhere.");
        }

        var cleaned = new List<string>();
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sounds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string raw in entries)
        {
            string entry = raw.Trim();

            if (entry.Length == 0)
            {
                throw new InvalidOperationException($"{field} has an empty entry.");
            }

            if (entry.Any(char.IsDigit))
            {
                throw new InvalidOperationException(
                    $"{field} entry '{entry}' has a digit in it. An invented name with a number in it " +
                    "reads as a record number and will be found again by the pattern rules.");
            }

            if (!folded.Add(Folding.StripAccents(entry)))
            {
                throw new InvalidOperationException(
                    $"{field} lists '{entry}' twice, ignoring accents and case. Two entries the vault " +
                    "cannot tell apart are one name that two subjects can be given.");
            }

            string sound = string.Join(' ', Tokenizer.Tokenize(entry).Select(t => PhoneticKey.Compute(t.Text)));
            if (sounds.TryGetValue(sound, out string? clash))
            {
                throw new InvalidOperationException(
                    $"{field} entries '{clash}' and '{entry}' sound alike to this matcher. Two surrogates " +
                    "it cannot tell apart merge two people the next time the corpus is read.");
            }

            sounds[sound] = entry;
            cleaned.Add(entry);
        }

        return cleaned;
    }

    private static SiluetaLineage LoadBuiltin()
    {
        const string resource = "Silueta.Core.Lineage.Lineages.lineage.core.json";
        using Stream? stream = typeof(SiluetaLineage).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The built-in lineage is not embedded in this build.");

        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    private string ComputeFingerprint()
    {
        var canonical = new StringBuilder("silueta-lineage/1\n");
        canonical.Append(Name).Append('\t').Append(Version).Append('\t').Append(Language).Append('\n');

        // Sorted: the order of a word list does not change what the list is, and the vault shuffles it
        // before minting anyway. Two lineages that differ only in line order are the same lineage.
        Append(canonical, "given", Pools.Given);
        Append(canonical, "family", Pools.Family);

        foreach ((IdentifierKind kind, string text) in Labels.OrderBy(p => p.Key))
        {
            canonical.Append("label\t").Append(kind).Append('\t').Append(text).Append('\n');
        }

        foreach ((IdentifierKind kind, string text) in Generalizations.OrderBy(p => p.Key))
        {
            canonical.Append("wider\t").Append(kind).Append('\t').Append(text).Append('\n');
        }

        foreach (PatternRule rule in Patterns.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            canonical.Append("rule\t").Append(rule.Id).Append('\t').Append(rule.Kind).Append('\t')
                .Append(rule.Regex).Append('\t')
                .Append(rule.Confidence.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())).AsSpan(0, 8));

        static void Append(StringBuilder builder, string label, IReadOnlyList<string> pool)
        {
            foreach (string entry in pool.OrderBy(e => e, StringComparer.Ordinal))
            {
                builder.Append(label).Append('\t').Append(entry).Append('\n');
            }
        }
    }
}
