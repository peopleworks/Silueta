using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Silueta.Core;

/// <summary>
/// What one run removed, and under which rules. This travels with the corpus: an expert determination
/// rests on the method being written down, and "we ran a redactor once" is not a method.
/// </summary>
public sealed class RedactionManifest
{
    public string RecordId { get; set; } = string.Empty;

    public string Policy { get; set; } = string.Empty;

    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>
    /// A digest of the rules that actually ran. Two corpora can both say <c>safe-harbor/0.1</c> and have
    /// been redacted under different actions or a different confidence floor; this is the field that
    /// tells them apart, and the one to compare before merging two corpora or reproducing a result.
    /// </summary>
    public string PolicyFingerprint { get; set; } = string.Empty;

    /// <summary>Which lineage produced this corpus: whose word lists, whose labels, whose rules.</summary>
    public string Lineage { get; set; } = string.Empty;

    public string LineageVersion { get; set; } = string.Empty;

    /// <summary>What language the lineage says it is for. Recorded, not acted on: the phonetic matcher
    /// is one compiled-in Spanish-and-English key regardless of what this says.</summary>
    public string LineageLanguage { get; set; } = string.Empty;

    /// <summary>A digest of the lineage's content. Two corpora can name the same lineage and the same
    /// version and have been redacted with different word lists; this is what tells them apart.</summary>
    public string LineageFingerprint { get; set; } = string.Empty;

    /// <summary>Keys in the lineage naming a kind this build does not know. Written down so that "no
    /// such kind" and "nothing to replace" are not the same silence.</summary>
    public List<string> LineageKeysSkipped { get; set; } = new();

    public string EngineVersion { get; set; } = string.Empty;

    public DateTimeOffset RunUtc { get; set; }

    public int TextLength { get; set; }

    public int Subjects { get; set; }

    /// <summary>How many spans of each <see cref="IdentifierKind"/> were replaced.</summary>
    public Dictionary<string, int> ByKind { get; set; } = new();

    /// <summary>Which detector found them. A pack that stops firing shows up here before it shows up
    /// in the leak rate.</summary>
    public Dictionary<string, int> ByDetector { get; set; } = new();

    /// <summary>Exact, phonetic, fuzzy, pattern. The phonetic and fuzzy counts are the ones that say
    /// how much ASR damage this corpus actually has.</summary>
    public Dictionary<string, int> ByMatch { get; set; } = new();

    /// <summary>
    /// How many identifiers this same pipeline can still find in its own output. Zero is the only
    /// acceptable value, and anything else is a run that should not be exported. It is not a leak rate:
    /// a name no detector can see is invisible here too.
    /// </summary>
    public int ResidualSpans { get; set; }

    /// <summary>
    /// SHA-256 of the transcript that went in, and of the one that came out.
    /// <para>
    /// Without these the manifest is bound to nothing: a reviewer holding a corpus and a manifest could
    /// not say the two belong together, and <c>TextLength</c> is a coincidence away from matching.
    /// </para>
    /// </summary>
    public string InputSha256 { get; set; } = string.Empty;

    public string OutputSha256 { get; set; } = string.Empty;

    /// <summary>A digest per detector of the rules it was carrying. The policy was fingerprinted and the
    /// rules that do the finding were not, so two corpora could claim one policy and be searched
    /// differently.</summary>
    public Dictionary<string, string> DetectorFingerprints { get; set; } = new();

    /// <summary>How many rules each detector loaded.</summary>
    public Dictionary<string, int> DetectorRulesLoaded { get; set; } = new();

    /// <summary>Rules this build could not load, by detector. Ids only. A rule skipped in silence makes
    /// "found nothing" and "never ran" the same entry.</summary>
    public Dictionary<string, List<string>> DetectorRulesSkipped { get; set; } = new();

    /// <summary>Kinds the policy was told to leave alone. A corpus redacted with StaffName kept produces
    /// counts identical to a transcript with no staff in it; this is the difference.</summary>
    public List<string> KeptKinds { get; set; } = new();
}

/// <summary>The de-identified text, what was replaced, and the manifest of the run.</summary>
/// <param name="Residue">What the same detectors still find in <paramref name="Text"/>.
/// <para>
/// Every safety rule in this library is enforced at the moment something is chosen: the surrogate the
/// roster would not match, the span that was replaced. None of them looked at the finished text — which
/// is the mistake the leak meter made, one level up, checking the decision instead of the result. A rule
/// enforced at choosing time is not the same as a rule that holds at emitting time: a surrogate minted
/// safely for one record is emitted unchanged into the next, whose roster it may well be on; and a
/// one-word surrogate can join the word after it and spell someone real.
/// </para>
/// <para>
/// So the engine reads its own output back. <b>A non-empty residue means this run should not be
/// exported.</b> An empty one is not proof of anything: residue is what this pipeline can see, so a name
/// it never knew about is missing from here too. It is a self-consistency check, not a leak rate.
/// </para></param>
public sealed record RedactionResult(
    string Text,
    IReadOnlyList<Detection> Applied,
    RedactionManifest Manifest,
    IReadOnlyList<Detection> Residue);

/// <summary>
/// The pipeline: detect, resolve overlaps, replace under policy, and write down what happened.
/// </summary>
public sealed partial class SiluetaEngine
{
    private readonly List<IDetector> _detectors;

    /// <param name="lineage">The word lists and the replacement text this run uses. Defaults to the one
    /// embedded in the build, which is what this library always did before an organisation could bring
    /// its own.</param>
    public SiluetaEngine(IEnumerable<IDetector> detectors, PseudonymVault? vault = null, SiluetaLineage? lineage = null)
    {
        _detectors = detectors.ToList();
        Lineage = lineage ?? SiluetaLineage.Default;
        Vault = vault ?? new PseudonymVault(Lineage.Pools);
    }

    /// <summary>Known values plus the core pattern pack: everything deterministic, nothing to download.</summary>
    public static SiluetaEngine CreateDefault() =>
        new([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()]);

    /// <summary>
    /// The same pipeline, reading its word lists, its labels and its pattern rules from a lineage the
    /// caller brought. A vault passed in keeps its own pools: the vault is what minted the names already
    /// in the corpus, and a lineage swapped underneath it does not rename anybody.
    /// </summary>
    public static SiluetaEngine FromLineage(SiluetaLineage lineage, PseudonymVault? vault = null)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        return new SiluetaEngine([new KnownValueDetector(), lineage.CreatePatternDetector()], vault, lineage);
    }

    public PseudonymVault Vault { get; }

    /// <summary>What this engine replaces with, and what it draws invented names from.</summary>
    public SiluetaLineage Lineage { get; }

    public RedactionResult Redact(string text, DeidentificationContext context, SiluetaPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);
        policy ??= SiluetaPolicy.SafeHarbor;

        var found = new List<Detection>();
        foreach (IDetector detector in _detectors)
        {
            found.AddRange(detector.Detect(text, context));
        }

        List<Detection> applied = Resolve(found, policy);

        // No invented name may be one this very run would detect, or the next pass over the output finds
        // the surrogate and replaces it again. The test is the detectors themselves rather than a second
        // copy of their threshold, because two copies of a rule are two rules that will disagree.
        bool WouldBeFound(string candidate) =>
            _detectors.Any(detector => detector.Detect(candidate, context).Any());

        // The record id and every subject id travel: one in the manifest that ships with the corpus, the
        // others as the keys of the vault. Both are documented as needing to be opaque, and that rule
        // lived only in a doc comment — so a caller could pass the patient's name, watch it removed from
        // the text, and publish it in the same run through the very file meant to prove it was not.
        // Checked with the detectors, which is the one test that cannot drift from the matcher.
        RejectIfItNamesSomeone(context.RecordId, "record id", WouldBeFound);
        foreach (string subjectId in context.Known.Select(known => known.SubjectId).Distinct(StringComparer.Ordinal))
        {
            RejectIfItNamesSomeone(subjectId, $"subject id '{Redacted(subjectId)}'", WouldBeFound);
        }

        var sb = new StringBuilder(text.Length);
        var subjects = new HashSet<string>(StringComparer.Ordinal);
        int cursor = 0;

        foreach (Detection detection in applied)
        {
            sb.Append(text, cursor, detection.Start - cursor);
            sb.Append(Replacement(detection, text, WouldBeFound, policy));
            cursor = detection.End;

            if (detection.SubjectId is { Length: > 0 } subjectId && subjects.Add(subjectId))
            {
                // Minting here keeps the vault complete: every person the text mentioned has an id,
                // whether or not the caller also has them in a structured field.
                Vault.PseudonymFor(subjectId);
            }
        }

        sb.Append(text, cursor, text.Length - cursor);
        string redacted = sb.ToString();

        // Read our own output back. Costs one more detection pass over a text of the same size, which is
        // a fair price for the only check that asks whether the work actually held.
        List<Detection> residue = Resolve(
            _detectors.SelectMany(detector => detector.Detect(redacted, context)).ToList(),
            policy);

        var manifest = new RedactionManifest
        {
            RecordId = context.RecordId,
            Policy = policy.Name,
            PolicyVersion = policy.Version,
            PolicyFingerprint = policy.Fingerprint,
            Lineage = Lineage.Name,
            LineageVersion = Lineage.Version,
            LineageLanguage = Lineage.Language,
            LineageFingerprint = Lineage.Fingerprint,
            LineageKeysSkipped = [.. Lineage.Skipped],
            EngineVersion = typeof(SiluetaEngine).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            RunUtc = DateTimeOffset.UtcNow,
            TextLength = text.Length,
            Subjects = subjects.Count,
            ResidualSpans = residue.Count,
            InputSha256 = Digest(text),
            OutputSha256 = Digest(redacted),
            KeptKinds = [.. Enum.GetValues<IdentifierKind>()
                .Where(kind => policy.ActionFor(kind) == RedactionAction.Keep)
                .Select(kind => kind.ToString())
                .Order(StringComparer.Ordinal)],
        };

        foreach (IDetector detector in _detectors)
        {
            if (detector is IDetectorProvenance provenance)
            {
                manifest.DetectorFingerprints[detector.Id] = provenance.Fingerprint;
                manifest.DetectorRulesLoaded[detector.Id] = provenance.RulesLoaded;
                manifest.DetectorRulesSkipped[detector.Id] = [.. provenance.RulesSkipped];
            }
        }

        foreach (Detection detection in applied)
        {
            Increment(manifest.ByKind, detection.Kind.ToString());
            Increment(manifest.ByDetector, detection.DetectorId);
            Increment(manifest.ByMatch, detection.Match.ToString());
        }

        return new RedactionResult(redacted, applied, manifest, residue);
    }

    /// <summary>
    /// Two detectors will find the same name, and a longer span usually contains a shorter one
    /// ("Sofia Reyes" over "Sofia"). Longest wins, then the most confident; what survives is a set of
    /// spans that do not touch, in reading order.
    /// </summary>
    private static List<Detection> Resolve(List<Detection> candidates, SiluetaPolicy policy)
    {
        List<Detection> ordered = candidates
            .Where(d => d.Confidence >= policy.MinConfidence && policy.ActionFor(d.Kind) != RedactionAction.Keep)
            .OrderByDescending(d => d.Length)
            .ThenByDescending(d => d.Confidence)
            .ThenBy(d => d.Start)
            .ToList();

        var accepted = new List<Detection>();
        foreach (Detection candidate in ordered)
        {
            bool clashes = false;
            foreach (Detection kept in accepted)
            {
                if (candidate.Overlaps(kept))
                {
                    clashes = true;
                    break;
                }
            }

            if (!clashes)
            {
                accepted.Add(candidate);
            }
        }

        accepted.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return accepted;
    }

    /// <summary>The original text is read here, from the transcript the caller passed in, rather than
    /// carried on the detection: see <see cref="Detection"/> for why that matters.</summary>
    private string Replacement(Detection detection, string source, Func<string, bool> wouldBeFound, SiluetaPolicy policy)
    {
        string original = detection.TextIn(source);

        return policy.ActionFor(detection.Kind) switch
        {
            RedactionAction.Surrogate when detection.SubjectId is { Length: > 0 } subjectId =>
                Vault.Pools.Fit(Vault.SurrogateFor(subjectId, wouldBeFound), Tokenizer.Tokenize(original).Count),
            RedactionAction.YearOnly => YearOf(original),
            RedactionAction.Generalize => Generalized(detection.Kind, original),
            RedactionAction.Keep => original,
            _ => LabelFor(detection.Kind),
        };
    }

    /// <summary>Safe Harbor keeps the year and nothing finer. A date with no year loses everything.</summary>
    private string YearOf(string text)
    {
        Match match = YearPattern().Match(text);
        return match.Success ? match.Value : LabelFor(IdentifierKind.Date);
    }

    /// <summary>
    /// Widening, where a wider value stops identifying. Postal codes are the awkward one: Safe Harbor
    /// allows the first three digits <em>only</em> where that three-digit area holds more than 20,000
    /// people, and requires the rest to become 000 — 45 CFR § 164.514(b)(2)(i)(B). Deciding which is
    /// which needs a census table with a date and a source on it, and there is not one in this package
    /// yet. Keeping three digits regardless would emit "036XX" for a Vermont prefix the rule names
    /// explicitly, inside a corpus whose manifest says safe-harbor. So the whole code goes until the
    /// table exists.
    /// </summary>
    private string Generalized(IdentifierKind kind, string original) => Lineage.GeneralizationFor(kind);

    /// <summary>
    /// What a removed identifier is replaced by. It was a <c>switch</c> here, in English, compiled in —
    /// which meant a Spanish-speaking agency's transcripts came back saying <c>[PHONE]</c> in the middle
    /// of a Spanish sentence, and nothing short of a fork could change it. It is the lineage's now.
    /// </summary>
    private string LabelFor(IdentifierKind kind) => Lineage.LabelFor(kind);

    /// <summary>Throws when an id that travels turns out to name one of the people it is hiding.</summary>
    private static void RejectIfItNamesSomeone(string id, string what, Func<string, bool> wouldBeFound)
    {
        if (wouldBeFound(id))
        {
            throw new ArgumentException(
                $"The {what} matches something on this record's roster, so it is not opaque. It travels — " +
                "the record id in the manifest, the subject id as the vault's key — and an id that names " +
                "the person publishes the identifier through the files meant to prove none were published. " +
                "Use an id of your own: \"r-042\", \"patient-1\", \"s-7f3\".");
        }
    }

    /// <summary>Shows the shape of an id without repeating it: the error must not echo what it rejected.</summary>
    private static string Redacted(string id) => id.Length <= 2 ? "…" : $"{id[0]}…{id[^1]}";

    private static string Digest(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void Increment(Dictionary<string, int> counter, string key) =>
        counter[key] = counter.TryGetValue(key, out int n) ? n + 1 : 1;

    [GeneratedRegex(@"\b(1[5-9]\d{2}|20\d{2})\b")]
    private static partial Regex YearPattern();
}
