namespace Silueta.Core;

/// <summary>
/// One way of running the pipeline over a corpus: which detectors, under what name. The name goes into the
/// report; the detectors do not, so a report can be read without the code that produced it.
/// </summary>
public sealed class EvaluationConfiguration
{
    private readonly Func<SiluetaLineage, IReadOnlyList<IDetector>> _detectors;

    public EvaluationConfiguration(string name, string description, Func<SiluetaLineage, IReadOnlyList<IDetector>> detectors)
    {
        Name = name;
        Description = description;
        _detectors = detectors;
    }

    /// <summary>What ships: known values heard through recogniser damage, plus the pattern pack.</summary>
    public static EvaluationConfiguration Silueta { get; } = new(
        "silueta", "roster matched by sound, plus the pattern pack",
        lineage => [new KnownValueDetector(), lineage.CreatePatternDetector()]);

    /// <summary>The first baseline: the same roster matched literally, plus the same pattern pack. The distance
    /// between this and <see cref="Silueta"/> is what the phonetic matching is worth.</summary>
    public static EvaluationConfiguration DenyList { get; } = new(
        "deny-list", "the same roster matched literally, plus the same pattern pack",
        lineage => [new DenyListDetector(), lineage.CreatePatternDetector()]);

    /// <summary>Ablation: the roster matcher on its own.</summary>
    public static EvaluationConfiguration KnownValuesOnly { get; } = new(
        "known-values-only", "roster matched by sound, no pattern rules",
        _ => [new KnownValueDetector()]);

    /// <summary>Ablation: the pattern pack on its own — what a run catches with no roster at all.</summary>
    public static EvaluationConfiguration PatternsOnly { get; } = new(
        "patterns-only", "pattern rules only, no roster",
        lineage => [lineage.CreatePatternDetector()]);

    public string Name { get; }

    public string Description { get; }

    internal IReadOnlyList<IDetector> DetectorsFor(SiluetaLineage lineage) => _detectors(lineage);
}

/// <summary>One document's result under one configuration. Offsets and counts only — never text.</summary>
public sealed record DocumentResult(string DocumentId, string Source, DeidScore Score);

/// <summary>One configuration's result over the whole corpus.</summary>
/// <param name="LeakRate">The headline: transcripts with anything left, with its interval.</param>
/// <param name="RecallByKind">Characters covered over characters marked, pooled across documents, by the
/// annotators' kind.</param>
public sealed record ConfigurationResult(
    string Name,
    string Description,
    LeakRateEstimate LeakRate,
    double PooledRecall,
    IReadOnlyDictionary<IdentifierKind, double> RecallByKind,
    int OverRedactedCharacters,
    IReadOnlyList<DocumentResult> Documents);

/// <summary>What the corpus is, written into the report so the number cannot travel without it.</summary>
public sealed record CorpusSummary(
    int Documents,
    IReadOnlyDictionary<string, int> Sources,
    int DocumentsWithTwoAnnotators,
    IReadOnlyList<string> Caveats);

/// <summary>An evaluation: the corpus it ran on, the build and lineage that ran, and a result per configuration.</summary>
public sealed record EvaluationReport(
    CorpusSummary Corpus,
    string EngineVersion,
    string Lineage,
    string LineageFingerprint,
    string PolicyFingerprint,
    IReadOnlyList<ConfigurationResult> Configurations);

/// <summary>
/// Runs the pipeline over a gold corpus and scores it, under as many configurations as asked.
/// <para>
/// Each configuration gets its own vault, shared across that configuration's documents, because that is how a
/// corpus is redacted for real — one subject, one invented name, all the way through. The report holds offsets,
/// counts and kinds, never a value from the corpus: this will be run on transcripts under an NDA, and the report
/// is the file most likely to be emailed around.
/// </para>
/// </summary>
public static class Evaluation
{
    public static EvaluationReport Run(GoldCorpus corpus, params EvaluationConfiguration[] configurations) =>
        Run(corpus, SiluetaLineage.Default, configurations);

    public static EvaluationReport Run(GoldCorpus corpus, SiluetaLineage lineage, params EvaluationConfiguration[] configurations)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(lineage);

        if (configurations.Length == 0)
        {
            throw new ArgumentException("An evaluation needs at least one configuration to run.", nameof(configurations));
        }

        var results = new List<ConfigurationResult>();
        foreach (EvaluationConfiguration configuration in configurations)
        {
            var vault = new PseudonymVault(lineage.Pools);
            var engine = new SiluetaEngine(configuration.DetectorsFor(lineage), vault, lineage);

            var documents = new List<DocumentResult>();
            foreach (GoldDocument document in corpus.Documents)
            {
                RedactionResult redaction = engine.Redact(document.Text, document.ToContext());
                DeidScore score = LeakRate.Score(
                    document.Text,
                    redaction.Text,
                    document.Spans.Select(span => span.ToDetection()),
                    redaction.Applied);

                documents.Add(new DocumentResult(document.DocumentId, document.Source, score));
            }

            List<DeidScore> scores = [.. documents.Select(d => d.Score)];

            var recallByKind = new SortedDictionary<IdentifierKind, double>();
            foreach (IdentifierKind kind in scores.SelectMany(s => s.ByKind.Keys).Distinct())
            {
                int sensitive = scores.Sum(s => s.ByKind.TryGetValue(kind, out KindScore? k) ? k.Sensitive : 0);
                int covered = scores.Sum(s => s.ByKind.TryGetValue(kind, out KindScore? k) ? k.Covered : 0);
                if (sensitive > 0)
                {
                    recallByKind[kind] = (double)covered / sensitive;
                }
            }

            results.Add(new ConfigurationResult(
                configuration.Name,
                configuration.Description,
                LeakRate.OfTranscripts(scores)!,
                LeakRate.PooledRecall(scores) ?? 1.0,
                recallByKind,
                scores.Sum(s => s.OverRedactedCharacters),
                documents));
        }

        return new EvaluationReport(
            Summarise(corpus),
            typeof(SiluetaEngine).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            $"{lineage.Name}/{lineage.Version}",
            lineage.Fingerprint,
            SiluetaPolicy.SafeHarbor.Fingerprint,
            results);
    }

    private static CorpusSummary Summarise(GoldCorpus corpus)
    {
        var sources = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (GoldDocument document in corpus.Documents)
        {
            sources[document.Source] = sources.GetValueOrDefault(document.Source) + 1;
        }

        int twoAnnotators = corpus.Documents.Count(d => d.Annotators.Count >= 2);
        var caveats = new List<string>();

        if (twoAnnotators < corpus.Documents.Count)
        {
            caveats.Add(
                $"{corpus.Documents.Count - twoAnnotators} of {corpus.Documents.Count} documents were marked by one annotator: " +
                "for those the number measures that annotator as much as the redactor, and agreement cannot be computed.");
        }

        if (corpus.Documents.Count < 30)
        {
            caveats.Add(
                $"{corpus.Documents.Count} documents is below the thirty this project set as a floor; read the interval, not the rate.");
        }

        if (sources.Count > 1)
        {
            caveats.Add("The corpus mixes sources. Read the result per source before reading it whole.");
        }

        return new CorpusSummary(corpus.Documents.Count, sources, twoAnnotators, caveats);
    }
}
