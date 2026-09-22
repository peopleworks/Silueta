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
    /// between this and <see cref="Silueta"/> is what the phonetic matching is worth.
    /// <para>
    /// Both run the rule for people named through a relationship ("my daughter Linda"), because it is part of the
    /// engine rather than of either matcher. Giving it to Silueta alone would have folded it into the difference
    /// the README labels "what matching by sound is worth", and that difference would then have been measuring
    /// two things under the name of one.
    /// </para></summary>
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
/// <param name="LeakedInScope">Whether anything this build has a way to find was left behind.</param>
/// <param name="SensitiveInScope">Marked characters of kinds in scope for this document.</param>
/// <param name="CoveredInScope">How many of those were covered.</param>
public sealed record DocumentResult(
    string DocumentId,
    string Source,
    DeidScore Score,
    bool LeakedInScope,
    int SensitiveInScope,
    int CoveredInScope);

/// <summary>One configuration's result over the whole corpus.</summary>
/// <param name="LeakRate">Transcripts with anything left, over every kind the annotators marked. Answers
/// "can this corpus leave the building?" — and measures missing rules as much as the matcher.</param>
/// <param name="LeakRateInScope">Transcripts with something left of a kind this build has a way to find: a
/// pattern rule, or the document's roster. Judges the matcher.</param>
/// <param name="RecallByKind">Characters covered over characters marked, pooled across documents, by the
/// annotators' kind.</param>
public sealed record ConfigurationResult(
    string Name,
    string Description,
    LeakRateEstimate LeakRate,
    double PooledRecall,
    LeakRateEstimate LeakRateInScope,
    double RecallInScope,
    IReadOnlyDictionary<IdentifierKind, double> RecallByKind,
    int OverRedactedCharacters,
    IReadOnlyList<DocumentResult> Documents);

/// <summary>A point estimate and a percentile bootstrap interval.</summary>
public sealed record Interval(double Estimate, double Lower, double Upper)
{
    /// <summary>"+0.057 [+0.021, +0.094]": signed, three decimals, invariant. One format, used by the command
    /// line and by the test that holds the README to a fresh evaluation, so the two cannot disagree on how a
    /// published difference is written.</summary>
    public override string ToString() => $"{Signed(Estimate)} [{Signed(Lower)}, {Signed(Upper)}]";

    private static string Signed(double value) =>
        value.ToString("+0.000;-0.000;0.000", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// One configuration against another over the same documents: the difference, with an interval from a paired
/// bootstrap that resamples documents, not characters. Characters within a document are not independent — a
/// name mentioned five times is one decision made five times — and an interval that pretended otherwise would
/// be narrower than the corpus can support.
/// </summary>
/// <param name="RecallDifference">Recall in scope, configuration minus baseline.</param>
/// <param name="LeakRateDifference">Leak rate in scope, configuration minus baseline. Negative is better.</param>
public sealed record PairedComparison(
    string Configuration,
    string Baseline,
    Interval RecallDifference,
    Interval LeakRateDifference,
    int Resamples,
    int Seed);

/// <summary>What the corpus is, written into the report so the number cannot travel without it.</summary>
public sealed record CorpusSummary(
    int Documents,
    IReadOnlyDictionary<string, int> Sources,
    int DocumentsWithTwoAnnotators,
    IReadOnlyList<string> Caveats);

/// <summary>An evaluation: the corpus it ran on, the build and lineage that ran, and a result per configuration.</summary>
/// <param name="Scope">How "in scope" was decided, in words, so the second number cannot be read without it.</param>
/// <param name="Comparisons">Silueta against the literal baseline, when both ran: the value of the thesis.</param>
public sealed record EvaluationReport(
    CorpusSummary Corpus,
    string EngineVersion,
    string Lineage,
    string LineageFingerprint,
    string PolicyFingerprint,
    string Scope,
    IReadOnlyList<ConfigurationResult> Configurations,
    IReadOnlyList<PairedComparison> Comparisons);

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

        // In scope is decided once, from the build that ships, and applied to every configuration alike: the
        // kinds the pattern pack has a rule for, plus the kinds on each document's roster. A rule that exists
        // and fails keeps its kind in scope. A place with no rule, or a person no roster names, is outside.
        IReadOnlyCollection<IdentifierKind> patternKinds = lineage.CreatePatternDetector().Kinds;

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

                var scope = new HashSet<IdentifierKind>(patternKinds);
                scope.UnionWith(document.Roster.Select(entry => entry.Kind));

                List<KindScore> inScope = [.. score.ByKind.Where(p => scope.Contains(p.Key)).Select(p => p.Value)];

                documents.Add(new DocumentResult(
                    document.DocumentId,
                    document.Source,
                    score,
                    LeakedInScope: inScope.Any(k => k.Covered < k.Sensitive || k.SurvivingSpans > 0),
                    SensitiveInScope: inScope.Sum(k => k.Sensitive),
                    CoveredInScope: inScope.Sum(k => k.Covered)));
            }

            List<DeidScore> scores = [.. documents.Select(d => d.Score)];
            int sensitiveInScope = documents.Sum(d => d.SensitiveInScope);

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
                new LeakRateEstimate(documents.Count, documents.Count(d => d.LeakedInScope)),
                sensitiveInScope == 0 ? 1.0 : (double)documents.Sum(d => d.CoveredInScope) / sensitiveInScope,
                recallByKind,
                scores.Sum(s => s.OverRedactedCharacters),
                documents));
        }

        List<PairedComparison> comparisons = [];
        ConfigurationResult? silueta = results.FirstOrDefault(r => r.Name == EvaluationConfiguration.Silueta.Name);
        ConfigurationResult? literal = results.FirstOrDefault(r => r.Name == EvaluationConfiguration.DenyList.Name);
        if (silueta is not null && literal is not null)
        {
            comparisons.Add(Compare(silueta, literal));
        }

        return new EvaluationReport(
            Summarise(corpus),
            typeof(SiluetaEngine).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            $"{lineage.Name}/{lineage.Version}",
            lineage.Fingerprint,
            SiluetaPolicy.SafeHarbor.Fingerprint,
            $"In scope: kinds the pattern pack has a rule for ({string.Join(", ", patternKinds.Order())}), " +
            "plus the kinds on each document's roster. A rule that exists and fails stays in scope.",
            results,
            comparisons);
    }

    /// <summary>
    /// A configuration against a baseline over the same documents, with a paired bootstrap: each resample draws
    /// documents with replacement and recomputes both sides on the same draw. The seed is fixed so the interval
    /// is part of what the repository reproduces, not a different number every run.
    /// </summary>
    public static PairedComparison Compare(ConfigurationResult configuration, ConfigurationResult baseline, int resamples = 1000, int seed = 20260916)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(baseline);

        List<(DocumentResult A, DocumentResult B)> pairs = [.. configuration.Documents.Join(
            baseline.Documents, a => a.DocumentId, b => b.DocumentId, (a, b) => (a, b), StringComparer.Ordinal)];

        if (pairs.Count == 0 || pairs.Count != configuration.Documents.Count || pairs.Count != baseline.Documents.Count)
        {
            throw new ArgumentException("A paired comparison needs both configurations run over the same documents.");
        }

        (double recall, double leak) Difference(IEnumerable<(DocumentResult A, DocumentResult B)> sample)
        {
            List<(DocumentResult A, DocumentResult B)> drawn = [.. sample];
            int sensitiveA = drawn.Sum(p => p.A.SensitiveInScope);
            int sensitiveB = drawn.Sum(p => p.B.SensitiveInScope);
            double recallA = sensitiveA == 0 ? 1.0 : (double)drawn.Sum(p => p.A.CoveredInScope) / sensitiveA;
            double recallB = sensitiveB == 0 ? 1.0 : (double)drawn.Sum(p => p.B.CoveredInScope) / sensitiveB;
            double leakA = (double)drawn.Count(p => p.A.LeakedInScope) / drawn.Count;
            double leakB = (double)drawn.Count(p => p.B.LeakedInScope) / drawn.Count;
            return (recallA - recallB, leakA - leakB);
        }

        (double recall, double leak) point = Difference(pairs);

        var random = new Random(seed);
        var recalls = new double[resamples];
        var leaks = new double[resamples];
        for (int r = 0; r < resamples; r++)
        {
            (double recall, double leak) = Difference(Enumerable.Range(0, pairs.Count).Select(_ => pairs[random.Next(pairs.Count)]));
            recalls[r] = recall;
            leaks[r] = leak;
        }

        Array.Sort(recalls);
        Array.Sort(leaks);

        return new PairedComparison(
            configuration.Name,
            baseline.Name,
            new Interval(point.recall, Percentile(recalls, 0.025), Percentile(recalls, 0.975)),
            new Interval(point.leak, Percentile(leaks, 0.025), Percentile(leaks, 0.975)),
            resamples,
            seed);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double position = p * (sorted.Length - 1);
        int below = (int)Math.Floor(position);
        int above = (int)Math.Ceiling(position);
        return below == above ? sorted[below] : sorted[below] + ((position - below) * (sorted[above] - sorted[below]));
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
