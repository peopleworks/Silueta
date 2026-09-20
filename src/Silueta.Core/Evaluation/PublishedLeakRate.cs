using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Silueta.Core;

/// <summary>
/// What this build measured about how often it leaves an identifier behind, embedded in the assembly so that
/// anything it produces can say so without a corpus, a file path or a network call.
/// <para>
/// It exists because of the manifest. The manifest is the artefact that travels with a redacted corpus — the
/// file a compliance officer opens, the one attached to an email — and until now it said what ran and never
/// how often what ran is wrong. A reader holding it had a list of counts and no way to weigh them, which is
/// the exact failure this project accuses everyone else of. The README carries the number for whoever reads
/// the repository; this carries it for whoever installed the package.
/// </para>
/// <para>
/// Written by <c>tools/Silueta.Calibration</c>, in the same run that rewrites the README's published block, so
/// the two cannot disagree about what was measured or when. <c>PublishedLeakRateTests</c> re-runs the
/// evaluation over the committed corpus and requires this to match it.
/// </para>
/// <para>
/// <b>It is a property of the build, not of a run.</b> A manifest that carried it as though it described its
/// own document would be worse than saying nothing: this number came from thirty synthetic transcripts, and
/// the run it is printed beside was never measured at all.
/// </para>
/// </summary>
public sealed record PublishedLeakRate
{
    /// <summary>The corpus this was measured against, e.g. "silueta-tts-asr".</summary>
    public required string CorpusId { get; init; }

    /// <summary>How many documents. The rate is a fraction of this, and a reader needs the denominator to
    /// know what the interval is going to look like.</summary>
    public required int Documents { get; init; }

    /// <summary>How each document was produced, and how many of each. "synthetic-tts" is not "real-redacted",
    /// and a single rate over a mixed corpus hides which one it came from.</summary>
    public required IReadOnlyDictionary<string, int> Sources { get; init; }

    /// <summary>Date of the measuring run, yyyy-MM-dd.</summary>
    public required string MeasuredOn { get; init; }

    /// <summary>The engine version that produced it.</summary>
    public required string Engine { get; init; }

    /// <summary>Whose word lists and rules ran: name and version.</summary>
    public required string Lineage { get; init; }

    /// <summary>A digest of the lineage's content. Two measurements can name one lineage and one version and
    /// have run different word lists; this is what tells them apart.</summary>
    public required string LineageFingerprint { get; init; }

    /// <summary>A digest of the policy that ran, for the same reason.</summary>
    public required string PolicyFingerprint { get; init; }

    /// <summary>How "in scope" was decided, in words, so <see cref="PublishedConfiguration.LeakingInScope"/>
    /// cannot be read without it.</summary>
    public required string Scope { get; init; }

    /// <summary>What is wrong with the corpus, carried alongside the number it produced. A rate whose caveats
    /// live in a different file is a rate that will be quoted without them.</summary>
    public IReadOnlyList<string> Caveats { get; init; } = [];

    /// <summary>What was measured, per configuration: the build itself, and the baselines it was compared to.</summary>
    public IReadOnlyList<PublishedConfiguration> Configurations { get; init; } = [];

    /// <summary>The build against the literal baseline — the value of matching by sound, which is the claim the
    /// project is built on. Null when the measuring run did not include a baseline to compare against.</summary>
    public PublishedComparison? Thesis { get; init; }

    /// <summary>One configuration by name, or null when this measurement did not include it.</summary>
    public PublishedConfiguration? For(string name) =>
        Configurations.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What this build shipped as, which is the configuration a manifest is talking about.</summary>
    [JsonIgnore]
    public PublishedConfiguration? Shipped => For(EvaluationConfiguration.Silueta.Name);

    /// <summary>
    /// The sentence a manifest carries: both rates, the corpus, the date and the engine, in one line and in one
    /// place. Phrased here rather than at each call site, because a number restated in three voices drifts in
    /// two of them — and the wording is the honest part. "Left something behind" is what was measured;
    /// "de-identified" is what nobody may say.
    /// </summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            PublishedConfiguration? shipped = Shipped;
            if (shipped is null)
            {
                return $"No measured leak rate for this build: {CorpusId} was measured on {MeasuredOn} without it.";
            }

            string sources = string.Join(", ", Sources.Select(s => $"{s.Value} {s.Key}"));
            return
                $"Measured on {CorpusId} ({sources}), {MeasuredOn}, engine {Engine}: {shipped.RateInScope} left " +
                $"something of a kind this build has a way to find, and {shipped.Rate} left something of any kind " +
                "the annotators marked. That is this build's rate on that corpus, not this run's.";
        }
    }

    /// <summary>
    /// The published shape of an evaluation. Counts, never percentages: <see cref="LeakRateEstimate"/> owns how
    /// a rate and its Wilson interval are computed, and a file that stored the percentage would be a second
    /// place where that arithmetic lives — the failure this repository has found in itself four times.
    /// </summary>
    public static PublishedLeakRate From(EvaluationReport report, string corpusId, string measuredOn)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusId);
        ArgumentException.ThrowIfNullOrWhiteSpace(measuredOn);

        if (!DateOnly.TryParseExact(measuredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException("A measurement date is written yyyy-MM-dd.", nameof(measuredOn));
        }

        return new PublishedLeakRate
        {
            CorpusId = corpusId,
            Documents = report.Corpus.Documents,
            Sources = new SortedDictionary<string, int>(report.Corpus.Sources.ToDictionary(s => s.Key, s => s.Value), StringComparer.Ordinal),
            MeasuredOn = measuredOn,
            Engine = report.EngineVersion,
            Lineage = report.Lineage,
            LineageFingerprint = report.LineageFingerprint,
            PolicyFingerprint = report.PolicyFingerprint,
            Scope = report.Scope,
            Caveats = [.. report.Corpus.Caveats],
            Configurations = [.. report.Configurations.Select(configuration => new PublishedConfiguration
            {
                Name = configuration.Name,
                Description = configuration.Description,
                Documents = configuration.LeakRate.Transcripts,
                Leaking = configuration.LeakRate.Leaking,
                LeakingInScope = configuration.LeakRateInScope.Leaking,
                RecallInScope = configuration.RecallInScope,
                PooledRecall = configuration.PooledRecall,
            })],
            Thesis = report.Comparisons
                .Where(c => c.Configuration == EvaluationConfiguration.Silueta.Name)
                .Select(c => new PublishedComparison
                {
                    Configuration = c.Configuration,
                    Baseline = c.Baseline,
                    RecallDifference = c.RecallDifference,
                    LeakRateDifference = c.LeakRateDifference,
                    Resamples = c.Resamples,
                    Seed = c.Seed,
                })
                .FirstOrDefault(),
        };
    }

    private static PublishedLeakRate? _current;
    private static bool _loaded;

    /// <summary>
    /// The measurement shipped with this build, or null when none was embedded or it could not be read — a
    /// legitimate state for a fork that has never measured itself, and one that callers must handle by saying
    /// so rather than by leaving the caveat out.
    /// <para>
    /// It never throws. A resource a fork hand-edited, a merge marker, an encoding slip: any of those would
    /// otherwise take down every redaction, from a property getter, over a footnote. A run that admits it has
    /// no measured rate is strictly better than no run.
    /// </para>
    /// <para>
    /// Cached including the null, so a missing resource does not rescan the manifest on every call. The race is
    /// benign: two threads may both read it, one assignment wins, and the value is immutable either way.
    /// </para>
    /// </summary>
    public static PublishedLeakRate? Current
    {
        get
        {
            if (_loaded)
            {
                return _current;
            }

            try
            {
                Assembly assembly = typeof(PublishedLeakRate).Assembly;
                string? name = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("published-leak-rate.json", StringComparison.Ordinal));

                if (name is not null)
                {
                    using Stream? stream = assembly.GetManifestResourceStream(name);
                    if (stream is not null)
                    {
                        _current = JsonSerializer.Deserialize(stream, SiluetaJsonContext.Default.PublishedLeakRate);
                    }
                }
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or IOException)
            {
                _current = null;
            }

            _loaded = true;
            return _current;
        }
    }
}

/// <summary>One configuration's published result: the build, or a baseline it was measured against.</summary>
public sealed record PublishedConfiguration
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int Documents { get; init; }

    /// <summary>Documents that still said something of any kind the annotators marked.</summary>
    public required int Leaking { get; init; }

    /// <summary>Documents that still said something of a kind this build has a way to find.</summary>
    public required int LeakingInScope { get; init; }

    public required double RecallInScope { get; init; }

    public required double PooledRecall { get; init; }

    /// <summary>The headline rate with its Wilson interval, computed from the counts rather than stored.</summary>
    [JsonIgnore]
    public LeakRateEstimate Rate => new(Documents, Leaking);

    /// <inheritdoc cref="Rate"/>
    [JsonIgnore]
    public LeakRateEstimate RateInScope => new(Documents, LeakingInScope);
}

/// <summary>A published difference between two configurations, with the interval of its paired bootstrap.</summary>
public sealed record PublishedComparison
{
    public required string Configuration { get; init; }

    public required string Baseline { get; init; }

    public required Interval RecallDifference { get; init; }

    /// <summary>Negative is better.</summary>
    public required Interval LeakRateDifference { get; init; }

    public required int Resamples { get; init; }

    public required int Seed { get; init; }
}
