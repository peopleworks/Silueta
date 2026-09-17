using System.Globalization;
using System.Text.Json;

namespace Silueta.Core;

/// <summary>
/// What a reader of a redacted corpus can put together across documents, keyed by invented name.
/// </summary>
/// <param name="Documents">Documents analysed.</param>
/// <param name="Subjects">Invented names that appear in at least one of them.</param>
/// <param name="SmallestClass">The size of the smallest group of subjects that share one pattern of
/// surviving quasi-identifiers — k, in k-anonymity. One means somebody's pattern is theirs alone. Null
/// when no invented name appears at all.</param>
/// <param name="UniqueSubjects">Subjects alone in their class.</param>
/// <param name="Blind">What this report cannot see, written into the report so it travels with the number.</param>
public sealed record LinkageReport(
    int Documents,
    int Subjects,
    int? SmallestClass,
    int UniqueSubjects,
    int MostDocumentsForOneSubject,
    double MedianDocumentsPerSubject,
    IReadOnlyList<SubjectExposure> BySubject,
    IReadOnlyList<string> Blind);

/// <summary>One invented name's footprint across the corpus.</summary>
/// <param name="Surrogate">The invented name, which the corpus already carries. Never the subject id, never
/// the code: those are the vault's, and a report holding them would be the way back filed beside the data.</param>
/// <param name="ClassSize">How many subjects, this one included, share exactly this pattern.</param>
public sealed record SubjectExposure(
    string Surrogate,
    int Documents,
    IReadOnlyList<string> Years,
    bool AgeBracket,
    IReadOnlyList<string> Kinship,
    int ClassSize);

/// <summary>The words that survive a redaction on purpose and still narrow down who a document is about.
/// Loaded from data, like the lineage: a clinic in another language brings its own.</summary>
public sealed class QuasiIdentifierVocabulary
{
    private static readonly Lazy<QuasiIdentifierVocabulary> Builtin = new(LoadBuiltin);

    public QuasiIdentifierVocabulary(IEnumerable<string> kinship, IEnumerable<string> ageBrackets)
    {
        Kinship = [.. kinship.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim())];
        AgeBrackets = [.. ageBrackets.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim())];
    }

    public static QuasiIdentifierVocabulary Default => Builtin.Value;

    public IReadOnlyList<string> Kinship { get; }

    public IReadOnlyList<string> AgeBrackets { get; }

    private static QuasiIdentifierVocabulary LoadBuiltin()
    {
        const string resource = "Silueta.Core.Evaluation.Vocabulary.quasi-identifiers.core.json";
        using Stream stream = typeof(QuasiIdentifierVocabulary).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The built-in quasi-identifier vocabulary is not embedded in this build.");

        QuasiIdentifierVocabularyFile file = JsonSerializer.Deserialize(stream, SiluetaJsonContext.Default.QuasiIdentifierVocabularyFile)
            ?? throw new InvalidOperationException("The built-in quasi-identifier vocabulary is empty.");

        return new QuasiIdentifierVocabulary(file.Kinship ?? [], file.AgeBrackets ?? []);
    }
}

/// <summary>
/// Cross-document exposure of a redacted corpus.
/// <para>
/// Every other measure in this library is per document, and the attack is not. The vault guarantees a
/// subject one invented name across the corpus — that is what makes cohorts and dashboards possible, and
/// it makes the invented name a join key: every visit, every kept year, every "90 or older", every "her
/// daughter", filed under one string. Whether a reader can then name the person is a test no code can run.
/// How many subjects share their pattern of surviving quasi-identifiers can be computed, and this computes
/// it.
/// </para>
/// </summary>
public static class Linkage
{
    private static readonly string[] BlindSpots =
    [
        "Places: no rule finds an address, a city or a postal code yet, so every place in the corpus survives and none is counted here.",
        "Clinical detail: diagnoses, medications and events are the analysis this library preserves, and they are quasi-identifiers this report does not see.",
        "Wording outside the vocabulary: kinship and age written in other forms or languages are not counted.",
        "Attribution by co-occurrence: every quasi-identifier in a document is attributed to every subject named in it. That makes patterns more distinct and classes smaller than a careful reader could prove — pessimistic by construction.",
        "A first name shared by two subjects is attributed to neither, because a reader cannot tell them apart either.",
        "An invented name retired by a remint is a separate key here, as it is to a reader who holds both halves of the corpus.",
    ];

    /// <summary>Analyses a redacted corpus. Runs where the vault is — inside the organisation — and produces a
    /// report that carries nothing the corpus does not already carry.</summary>
    /// <returns>Null for an empty corpus: a report of nothing is not a clean report.</returns>
    public static LinkageReport? Analyze(
        IEnumerable<(string DocumentId, string RedactedText)> documents,
        PseudonymVault vault,
        QuasiIdentifierVocabulary? vocabulary = null,
        SiluetaLineage? lineage = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(vault);

        List<(string DocumentId, string RedactedText)> corpus = documents.ToList();
        if (corpus.Count == 0)
        {
            return null;
        }

        vocabulary ??= QuasiIdentifierVocabulary.Default;

        // What the engine actually writes for an age over 89 is the lineage's, so it is read from there as
        // well as from the vocabulary: two lists of "what the age bracket says" would drift apart.
        List<string> brackets = [.. vocabulary.AgeBrackets];
        brackets.Add((lineage ?? SiluetaLineage.Default).GeneralizationFor(IdentifierKind.AgeOver89));

        List<Pattern> patterns = Patterns(vault);
        var seen = new Dictionary<string, Footprint>(StringComparer.Ordinal);

        foreach ((_, string text) in corpus)
        {
            List<Token> tokens = Tokenizer.Tokenize(text);

            var years = tokens.Select(t => t.Text).Where(IsYear).ToHashSet(StringComparer.Ordinal);
            bool bracket = brackets.Any(b => b.Length > 0 && Folding.Contains(text, b));
            var kinship = tokens
                .SelectMany(t => vocabulary.Kinship.Where(word => Folding.SameLetters(t.Text, word)))
                .ToHashSet(StringComparer.Ordinal);

            foreach (Pattern pattern in patterns)
            {
                if (!pattern.Forms.Any(form => Occurs(tokens, form)))
                {
                    continue;
                }

                Footprint footprint = seen.TryGetValue(pattern.Surrogate, out Footprint? known)
                    ? known
                    : seen[pattern.Surrogate] = new Footprint();

                footprint.Documents++;
                footprint.Years.UnionWith(years);
                footprint.AgeBracket |= bracket;
                footprint.Kinship.UnionWith(kinship);
            }
        }

        var signatures = seen.ToDictionary(p => p.Key, p => p.Value.Signature(), StringComparer.Ordinal);
        var classSizes = signatures.Values
            .GroupBy(signature => signature, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        List<SubjectExposure> bySubject = [.. seen
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new SubjectExposure(
                p.Key,
                p.Value.Documents,
                [.. p.Value.Years.Order(StringComparer.Ordinal)],
                p.Value.AgeBracket,
                [.. p.Value.Kinship.Order(StringComparer.Ordinal)],
                classSizes[signatures[p.Key]]))];

        List<int> counts = [.. bySubject.Select(s => s.Documents).Order()];

        return new LinkageReport(
            corpus.Count,
            bySubject.Count,
            bySubject.Count == 0 ? null : bySubject.Min(s => s.ClassSize),
            bySubject.Count(s => s.ClassSize == 1),
            counts.Count == 0 ? 0 : counts[^1],
            Median(counts),
            bySubject,
            BlindSpots);
    }

    /// <summary>The forms each invented name can take in a document: in full, and by its head when no other
    /// subject shares that head.</summary>
    private static List<Pattern> Patterns(PseudonymVault vault)
    {
        List<string> surrogates = [.. vault.Surrogates];
        var heads = surrogates.ToDictionary(s => s, s => vault.Pools.HeadOf(s), StringComparer.Ordinal);

        var patterns = new List<Pattern>();
        foreach (string surrogate in surrogates)
        {
            var forms = new List<string[]> { Words(surrogate) };

            string head = heads[surrogate];
            bool headIsUnique = !string.Equals(head, surrogate, StringComparison.OrdinalIgnoreCase)
                && heads.Count(h => Folding.SameLetters(h.Value, head)) == 1;

            if (headIsUnique)
            {
                forms.Add(Words(head));
            }

            patterns.Add(new Pattern(surrogate, forms));
        }

        return patterns;
    }

    private static string[] Words(string text) => [.. Tokenizer.Tokenize(text).Select(t => t.Text)];

    private static bool Occurs(List<Token> tokens, string[] form)
    {
        for (int i = 0; i + form.Length <= tokens.Count; i++)
        {
            bool all = true;
            for (int k = 0; k < form.Length && all; k++)
            {
                all = Folding.SameLetters(tokens[i + k].Text, form[k]);
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsYear(string word) =>
        word.Length == 4
        && word.All(char.IsAsciiDigit)
        && int.Parse(word, CultureInfo.InvariantCulture) is >= 1900 and <= 2099;

    private static double Median(List<int> sorted) => sorted.Count switch
    {
        0 => 0,
        _ when sorted.Count % 2 == 1 => sorted[sorted.Count / 2],
        _ => (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2.0,
    };

    private sealed record Pattern(string Surrogate, List<string[]> Forms);

    private sealed class Footprint
    {
        public int Documents { get; set; }

        public HashSet<string> Years { get; } = new(StringComparer.Ordinal);

        public bool AgeBracket { get; set; }

        public HashSet<string> Kinship { get; } = new(StringComparer.Ordinal);

        public string Signature() =>
            $"{string.Join(',', Years.Order(StringComparer.Ordinal))}|{AgeBracket}|{string.Join(',', Kinship.Order(StringComparer.Ordinal))}";
    }
}
