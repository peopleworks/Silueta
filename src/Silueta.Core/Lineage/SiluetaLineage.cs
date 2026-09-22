using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
public sealed partial class SiluetaLineage
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
        IReadOnlyDictionary<string, SiluetaPolicy> policies,
        IReadOnlyDictionary<IdentifierKind, IReadOnlyList<string>> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>> lists,
        IReadOnlyList<string> skipped)
    {
        Name = name;
        Version = version;
        Language = language;
        Pools = pools;
        Labels = labels;
        Generalizations = generalizations;
        Patterns = patterns;
        Policies = policies;
        Values = values;
        Lists = lists;
        Skipped = skipped;
        Fingerprint = ComputeFingerprint();
    }

    /// <summary>The lineage that ships with the library: the pools and labels this project has always
    /// had, as data rather than as code.</summary>
    public static SiluetaLineage Default => Builtin.Value;

    /// <summary>
    /// The built-in lineage exactly as it ships, comments and all — the file to start your own from.
    /// <para>
    /// Writing a lineage from a blank page means rediscovering which keys exist and which pools are
    /// required; this hands over the one that is known to load, with the notes that say why each part is the
    /// way it is. <c>silueta lineage &gt; mine.json</c> is the whole workflow.
    /// </para>
    /// </summary>
    public static string DefaultJson => BuiltinJson.Value;

    private static readonly Lazy<string> BuiltinJson = new(ReadBuiltinJson);

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

    /// <summary>
    /// The policies this organisation wrote, by name — not including Safe Harbor, which is compiled in once and
    /// is always available as "safe-harbor".
    /// <para>
    /// Pedro, 22 September 2026: dates, ages, diagnoses, measurements, state and city are statistics and must
    /// not be lost; what matters is not knowing whose they are — and that is what "los diccionarios y la
    /// configuración dinámica" were for. So an organisation can say, in its own file, what it keeps. Each
    /// policy is a set of departures from Safe Harbor rather than a table from nothing: a kind it does not name
    /// keeps Safe Harbor's action, so a file that forgets a kind removes more, not less. And the engine writes
    /// every departure into the manifest, so the file is not needed to see what a corpus was redacted under.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, SiluetaPolicy> Policies { get; }

    /// <summary>Every policy this lineage can run, Safe Harbor first.</summary>
    public IReadOnlyList<string> PolicyNames =>
        [SiluetaPolicy.SafeHarbor.Name, .. Policies.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// A policy by name. Asking for one the lineage does not have is an error rather than a fall-back to Safe
    /// Harbor: falling back is the safe direction for the text and the wrong one for the operator, who would
    /// then describe a corpus as redacted under a policy it was not.
    /// </summary>
    public SiluetaPolicy Policy(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.Equals(name, SiluetaPolicy.SafeHarbor.Name, StringComparison.Ordinal))
        {
            return SiluetaPolicy.SafeHarbor;
        }

        return Policies.TryGetValue(name, out SiluetaPolicy? policy)
            ? policy
            : throw new InvalidOperationException(
                $"The lineage '{Name}' has no policy called '{name}'. It has: {string.Join(", ", PolicyNames)}.");
    }

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

        var skipped = new List<string>();
        SurrogatePools pools = ReadPools(file.Pools, skipped);

        return new SiluetaLineage(
            file.Lineage.Trim(),
            file.Version.Trim(),
            string.IsNullOrWhiteSpace(file.Language) ? "und" : file.Language.Trim(),
            pools,
            Parse(file.Labels, skipped),
            WithoutPostalCode(Parse(file.Generalizations, skipped), skipped),
            file.Patterns ?? [],
            ReadPolicies(file.Policies, skipped),
            ReadValues(file.Values, skipped),
            ReadLists(file.Lists),
            skipped);
    }

    /// <summary>
    /// Values that identify in every record this organisation redacts, by kind — the cities it serves, found
    /// by the same matcher as the roster, so a lower-case transcript and a misheard "Scotsdale" are found too.
    /// Each run adds them to its roster with no subject, so each becomes the kind's label.
    /// <para>
    /// Never a person. A person needs a subject so that the same person gets the same invented name across a
    /// corpus, and a list shared by every record has none; people belong on each record's roster. A value that
    /// is also an ordinary word — Mesa, Surprise, Casa Grande — is removed wherever the word appears: the list
    /// is the organisation's, and so is that trade.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<IdentifierKind, IReadOnlyList<string>> Values { get; }

    /// <summary>
    /// Word lists this lineage's rules may name — <c>{{departamento-co}}</c> — beside the ones compiled into the
    /// build, which its rules may name too.
    /// <para>
    /// The rules that ship are built from American and Mexican standards, and this is how a project somewhere
    /// else writes its own without spelling its country into every regular expression. A name the build already
    /// carries is refused rather than replaced: one name for two lists is the second copy of a rule, and this
    /// copy would decide what gets found.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; }

    /// <summary>Reads a lineage from disk. The file is the organisation's, so everything about it is
    /// checked rather than assumed.</summary>
    public static SiluetaLineage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromJson(File.ReadAllText(path));
    }

    /// <summary>The detectors this lineage asks for: its own pattern rules, or the built-in pack. Its own rules
    /// are compiled with its own word lists as well as the build's.</summary>
    public PatternDetector CreatePatternDetector() =>
        Patterns.Count > 0 ? new PatternDetector(Patterns, lists: Lists) : PatternDetector.FromEmbeddedPack();

    /// <summary>What this lineage puts in place of a kind that is labelled away.</summary>
    public string LabelFor(IdentifierKind kind) =>
        Labels.TryGetValue(kind, out string? label) ? label : "[REMOVED]";

    /// <summary>What this lineage puts in place of a kind that is widened. Falls through to the label:
    /// a kind with no wider form still has to be removed, not kept.</summary>
    public string GeneralizationFor(IdentifierKind kind) =>
        Generalizations.TryGetValue(kind, out string? wider) ? wider : LabelFor(kind);

    /// <summary>
    /// The organisation's policies, each built on Safe Harbor. Strict about what it cannot interpret and lenient
    /// only where leniency removes more: an action it does not know is refused — "Kepp" could have meant Keep,
    /// and guessing is how a date goes out — while a kind it does not know is skipped and written down, and that
    /// kind keeps Safe Harbor's action, which is the safe way to fall.
    /// </summary>
    private static IReadOnlyDictionary<string, SiluetaPolicy> ReadPolicies(
        Dictionary<string, PolicyFile>? entries, List<string> skipped)
    {
        if (entries is null || entries.Count == 0)
        {
            return FrozenDictionary<string, SiluetaPolicy>.Empty;
        }

        var policies = new Dictionary<string, SiluetaPolicy>(StringComparer.Ordinal);
        foreach ((string rawName, PolicyFile file) in entries)
        {
            string name = rawName.Trim();
            if (string.Equals(name, SiluetaPolicy.SafeHarbor.Name, StringComparison.OrdinalIgnoreCase))
            {
                // The one name a file may not use. A policy that called itself "safe-harbor" while keeping dates
                // would put that name on every manifest over rules that are not it — the failure the immutable
                // policy was built to prevent, reached by editing a text file instead of a dictionary.
                throw new InvalidOperationException(
                    "\"safe-harbor\" is reserved for the table compiled into this build. A policy that departs " +
                    "from it needs a name of its own, so no manifest can carry Safe Harbor's name over other rules.");
            }

            if (name.Length == 0 || file is null || string.IsNullOrWhiteSpace(file.Version))
            {
                throw new InvalidOperationException(
                    $"The policy '{name}' needs a name and a version: the manifest records both, and a policy " +
                    "without them cannot be told apart from the next edit of itself.");
            }

            var actions = new Dictionary<IdentifierKind, RedactionAction>(SiluetaPolicy.SafeHarbor.Actions);
            foreach ((string key, string value) in file.Actions ?? [])
            {
                if (!IdentifierKindExtensions.TryParseName(key, out IdentifierKind kind))
                {
                    skipped.Add($"policies.{name}.{key}");
                    continue;
                }

                // By name and only by name, the way kinds are read (E5): Enum.TryParse also accepts "4" and
                // "Label, Keep" — which for this enum is Keep — and a policy file is the last place to learn that.
                RedactionAction? parsed = Enum.GetValues<RedactionAction>()
                    .Select(a => (RedactionAction?)a)
                    .FirstOrDefault(a => string.Equals(a.ToString(), value?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (parsed is not { } action)
                {
                    throw new InvalidOperationException(
                        $"The policy '{name}' asks for '{value}' for {key}, which this build does not know. " +
                        $"Actions: {string.Join(", ", Enum.GetNames<RedactionAction>())}.");
                }

                actions[kind] = action;
            }

            policies[name] = new SiluetaPolicy
            {
                Name = name,
                Version = file.Version.Trim(),
                MinConfidence = file.MinConfidence ?? SiluetaPolicy.SafeHarbor.MinConfidence,
                Actions = actions,
            };
        }

        return policies.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// A postal code's generalisation is not the lineage's to write. What it keeps is Safe Harbor's rule applied
    /// to the census's count (<see cref="CensusZipTable"/>), and a fixed text in its place would be a second rule
    /// for the same kind — one the manifest could not see, because it would still name the census table. An
    /// organisation that wants the whole code gone says so in a policy, <c>"PostalCode": "Label"</c>, where it is
    /// listed as a departure. The entry is skipped and written down, not refused: the rest of the file is fine.
    /// </summary>
    private static IReadOnlyDictionary<IdentifierKind, string> WithoutPostalCode(
        IReadOnlyDictionary<IdentifierKind, string> generalizations, List<string> skipped)
    {
        if (!generalizations.ContainsKey(IdentifierKind.PostalCode))
        {
            return generalizations;
        }

        skipped.Add("generalizations.PostalCode");
        return generalizations.Where(entry => entry.Key != IdentifierKind.PostalCode).ToFrozenDictionary();
    }

    /// <summary>
    /// The <c>lists</c> section. Two refusals rather than a skip, because a list is not a rule this build might
    /// be too old to know: it is a name the lineage's own rules use. A name the build already has would make two
    /// lists answer to one name; a name no placeholder could write — <c>{{...}}</c> reads lower-case letters,
    /// digits and hyphens — is a list nothing can name, which is a typo with no other explanation.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadLists(Dictionary<string, JsonElement>? raw)
    {
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach ((string name, JsonElement value) in raw ?? [])
        {
            if (name.StartsWith('_'))
            {
                continue; // a comment, by the convention the built-in file teaches
            }

            if (PatternLists.Names.Contains(name))
            {
                throw new InvalidOperationException(
                    $"lists.{name}: this build already has a list called \"{name}\", and a rule naming it would " +
                    "have two answers. Call yours something else — the build's lists are available to your rules " +
                    "as they are.");
            }

            if (!LineageListName().IsMatch(name))
            {
                throw new InvalidOperationException(
                    $"lists.{name}: a list's name is what a rule writes between double braces, so it may hold " +
                    "lower-case letters, digits and hyphens only.");
            }

            if (value.ValueKind != JsonValueKind.Array ||
                value.EnumerateArray().Any(static e => e.ValueKind != JsonValueKind.String))
            {
                throw new InvalidOperationException($"lists.{name} has to be a list of words.");
            }

            string[] words = [.. value.EnumerateArray()
                .Select(static e => e.GetString()!.Trim())
                .Where(static w => w.Length > 0)
                .Distinct(StringComparer.Ordinal)];

            if (words.Length == 0)
            {
                throw new InvalidOperationException($"lists.{name} is empty. A rule naming it would match nothing.");
            }

            lists[name] = words;
        }

        return lists.ToFrozenDictionary(StringComparer.Ordinal);
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex LineageListName();

    /// <summary>
    /// The <c>values</c> section. A kind this build does not know is skipped and written down, as everywhere
    /// in the file; so is a person, for the reason on <see cref="Values"/>. A value that is not a list of
    /// strings is refused: it is a mistake in the file, and guessing what it meant is how a city goes out.
    /// </summary>
    private static IReadOnlyDictionary<IdentifierKind, IReadOnlyList<string>> ReadValues(
        Dictionary<string, JsonElement>? raw, List<string> skipped)
    {
        var values = new Dictionary<IdentifierKind, IReadOnlyList<string>>();

        foreach ((string key, JsonElement value) in raw ?? [])
        {
            if (key.StartsWith('_'))
            {
                continue; // a comment, by the convention the built-in file teaches
            }

            if (!IdentifierKindExtensions.TryParseName(key, out IdentifierKind kind) || kind.IsPersonName())
            {
                skipped.Add($"values.{key}");
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array ||
                value.EnumerateArray().Any(static e => e.ValueKind != JsonValueKind.String))
            {
                throw new InvalidOperationException($"values.{key} has to be a list of strings.");
            }

            values[kind] = [.. value.EnumerateArray()
                .Select(static e => e.GetString()!.Trim())
                .Where(static v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)];
        }

        return values.ToFrozenDictionary();
    }

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
            if (!IdentifierKindExtensions.TryParseName(key, out IdentifierKind kind))
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
    /// <summary>
    /// The pools, read from a map whose names this build recognises. The people's pair is required; the
    /// others are optional, and a kind whose pool is absent is labelled rather than given a name from
    /// somebody else's pool.
    /// </summary>
    private static SurrogatePools ReadPools(Dictionary<string, JsonElement>? raw, List<string> skipped)
    {
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, JsonElement value) in raw ?? [])
        {
            if (name.StartsWith('_'))
            {
                continue; // a comment, by the convention the built-in file teaches
            }

            if (!SurrogatePools.Recognised.TryGetValue(name, out string? canonical))
            {
                // A pool a newer build would use. Recorded, so "this build has no such pool" and "the
                // lineage did not bring one" are not the same silence.
                skipped.Add($"pools.{name}");
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array ||
                value.EnumerateArray().Any(static e => e.ValueKind != JsonValueKind.String))
            {
                throw new InvalidOperationException($"pools.{name} has to be a list of names.");
            }

            if (lists.ContainsKey(canonical))
            {
                throw new InvalidOperationException(
                    $"pools.{name} is given twice, differing only in case. Which one is meant is not a " +
                    "guess this loader makes.");
            }

            lists[canonical] = Clean(
                [.. value.EnumerateArray().Select(static e => e.GetString()!)],
                $"pools.{name}",
                allowDigits: canonical is SurrogatePools.ProductPool or SurrogatePools.ProductSuffixPool);
        }

        foreach (string required in (string[])[SurrogatePools.GivenPool, SurrogatePools.FamilyPool])
        {
            if (!lists.ContainsKey(required))
            {
                throw new InvalidOperationException(
                    $"A lineage needs pools.{required}: a person's invented name has to come from somewhere.");
            }
        }

        RefuseHeadsMintableAsAnotherKind(lists);
        return new SurrogatePools(lists);
    }

    /// <summary>
    /// A head that another kind's pools can also produce, word for word, is refused.
    /// <para>
    /// The vault finds where a surrogate's head ends by asking every head pool for the longest entry the
    /// name starts with. Put "Cruz Medina" in the company pool beside a given name "Cruz" and a family
    /// name "Medina", and a <em>person</em> already minted as "Cruz Medina" suddenly has a two-word head:
    /// a one-word mention of them is replaced by two words, and the vault reserves a head it never minted.
    /// A vault that was fine yesterday is corrupted by adding a company name today, so the loader refuses
    /// the company name and says which one.
    /// </para>
    /// </summary>
    private static void RefuseHeadsMintableAsAnotherKind(Dictionary<string, IReadOnlyList<string>> lists)
    {
        (string Head, string Tail)[] pairs =
        [
            (SurrogatePools.GivenPool, SurrogatePools.FamilyPool),
            (SurrogatePools.CompanyPool, SurrogatePools.CompanySuffixPool),
            (SurrogatePools.ProductPool, SurrogatePools.ProductSuffixPool),
        ];

        HashSet<string> SetOf(string pool) => new(
            lists.TryGetValue(pool, out IReadOnlyList<string>? entries) ? entries : [],
            StringComparer.OrdinalIgnoreCase);

        foreach ((string head, _) in pairs)
        {
            if (!lists.TryGetValue(head, out IReadOnlyList<string>? entries))
            {
                continue;
            }

            foreach (string entry in entries)
            {
                string[] words = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach ((string otherHead, string otherTail) in pairs)
                {
                    if (otherHead == head)
                    {
                        continue;
                    }

                    HashSet<string> heads = SetOf(otherHead);
                    HashSet<string> tails = SetOf(otherTail);

                    for (int split = 1; split < words.Length; split++)
                    {
                        string front = string.Join(' ', words[..split]);
                        string back = string.Join(' ', words[split..]);

                        if (heads.Contains(front) && tails.Contains(back))
                        {
                            throw new InvalidOperationException(
                                $"pools.{head} entry '{entry}' can also be minted from pools.{otherHead} " +
                                $"('{front}') and pools.{otherTail} ('{back}'). A name two kinds can both " +
                                "produce changes where the other kind's invented names end, and corrupts a " +
                                "vault that already holds one.");
                        }
                    }
                }
            }
        }
    }

    private static IReadOnlyList<string> Clean(List<string> entries, string field, bool allowDigits = false)
    {
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"{field} is empty. Declare a pool with names in it, or leave it out.");
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

            // Products are the exception: "Serie 7" is a product name. The rule that matters still holds
            // where it is enforced — the vault never emits a name the pattern rules would find again.
            if (!allowDigits && entry.Any(char.IsDigit))
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

    private static SiluetaLineage LoadBuiltin() => FromJson(DefaultJson);

    private static string ReadBuiltinJson()
    {
        const string resource = "Silueta.Core.Lineage.Lineages.lineage.core.json";
        using Stream? stream = typeof(SiluetaLineage).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The built-in lineage is not embedded in this build.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private string ComputeFingerprint()
    {
        var canonical = new StringBuilder("silueta-lineage/1\n");
        canonical.Append(Name).Append('\t').Append(Version).Append('\t').Append(Language).Append('\n');

        // Every pool, by name — not given and family by hand, which would give two lineages differing only
        // in their company names one digest. Sorted: the order of a word list does not change what the
        // list is, and the vault shuffles it before minting anyway.
        foreach ((string name, IReadOnlyList<string> pool) in Pools.All.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Append(canonical, name, pool);
        }

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

        // Only when there are any, so that a lineage with no policies — the built-in one among them — digests to
        // exactly what it did before policies existed, and every manifest already written still matches.
        foreach ((string name, SiluetaPolicy policy) in Policies.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            canonical.Append("policy\t").Append(name).Append('\t').Append(policy.Fingerprint).Append('\n');
        }

        // The same rule: nothing when there are none.
        foreach ((IdentifierKind kind, IReadOnlyList<string> list) in Values.OrderBy(p => p.Key))
        {
            foreach (string value in list.Order(StringComparer.Ordinal))
            {
                canonical.Append("value\t").Append(kind).Append('\t').Append(value).Append('\n');
            }
        }

        foreach ((string name, IReadOnlyList<string> list) in Lists.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (string word in list.Order(StringComparer.Ordinal))
            {
                canonical.Append("list\t").Append(name).Append('\t').Append(word).Append('\n');
            }
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
