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

    /// <summary>Kinds the policy asked to replace with an invented name and that were labelled instead,
    /// because the lineage had no pool to draw one from. A label where the policy said surrogate is a
    /// decision the run made; a manifest that stayed quiet about it would describe a policy that did not
    /// happen.</summary>
    public List<string> SurrogatesUnavailable { get; set; } = new();

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

    /// <summary>
    /// How often the build that produced this manifest is known to leave an identifier behind, measured on the
    /// corpus named in the sentence — <b>a property of the build, never of this document</b>.
    /// <para>
    /// Every other field here says what ran. None of them says how often what ran is wrong, and this is the
    /// file a compliance officer opens: a page of counts with no error rate is the overclaim this project
    /// exists to argue against. See <see cref="PublishedLeakRate"/>.
    /// </para>
    /// <para>
    /// The default is the admission, not an empty string. Three times already a required field in this library
    /// defaulted to something harmless-looking and the harmless value was the defect — a roster entry with no
    /// kind became a person, an empty JSON object parsed as a valid vault. A manifest whose leak rate is
    /// missing reads as a run that did not leak, and of all the fields here that is the worst one to guess at.
    /// </para>
    /// </summary>
    public string MeasuredLeakRate { get; set; } = NoMeasurement;

    /// <summary>
    /// Spans that were redacted without being attributed to anybody, because two candidates covering them
    /// named different subjects or two names crossed. Each one is a place where the corpus loses the thread
    /// between documents, and a run that quietly stopped attributing would otherwise look like a run with
    /// fewer people in it.
    /// </summary>
    public int AmbiguousAttributions { get; set; }

    /// <summary>
    /// People the transcript named through a relationship — "my daughter Linda" — who were not on the roster.
    /// Every mention of them was replaced with a label rather than an invented name, because nobody gave them a
    /// subject and the run will not invent one from the text. So they are de-identified, and they cannot be
    /// followed across the corpus; this is how many people that happened to.
    /// </summary>
    public int UnrosteredPeople { get; set; }

    /// <summary>
    /// The rule that looked for those people and the words it read — "kinship/1" and a digest — or <c>off</c>.
    /// A rule that decides what gets found belongs in the manifest for the same reason the matcher's tolerance
    /// does: two corpora searched differently must not look alike.
    /// </summary>
    public string RelativesRule { get; set; } = "off";

    /// <summary>
    /// Every way the policy that ran differs from Safe Harbor — "Date: Keep (Safe Harbor: YearOnly)" — and empty
    /// when it is Safe Harbor. The policy name says what the organisation called its rules; this says what they
    /// were, so a compliance reader holding the manifest does not also need the lineage file to see that a
    /// corpus kept its dates.
    /// </summary>
    public List<string> DeparturesFromSafeHarbor { get; set; } = new();

    /// <inheritdoc cref="MeasuredLeakRate"/>
    public const string NoMeasurement =
        "No measured leak rate: this build carries no calibration, so nothing here says how often it leaves an identifier behind.";
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

    /// <summary>
    /// Whether the run also looks for people the transcript names through a relationship — "my daughter Linda",
    /// "su hija Lucía" — and treats them as on the roster for that one run. On by default: a privacy tool that
    /// has to be asked before it stops leaking a relative is set the wrong way round.
    /// <para>
    /// What it does with them is the whole design. A name the caller's roster already finds is left to the
    /// roster: a listed relative keeps her subject and her invented name. A name it does not find is added to
    /// this run's roster with <b>no subject</b>, so the matcher finds every mention of it — "Linda said she would
    /// call" two sentences later, and "Lynda" through recogniser damage — and each becomes a label. Nobody is
    /// invented: the agency never listed this person, and a subject minted from the text would put a derivative
    /// of a real name into the vault's keys. Coreference is lost for them, and the manifest counts them.
    /// </para>
    /// <para>
    /// It is not a detector, and that matters twice. A detector that fired on the word after "daughter" would
    /// catch only that one mention, and a document that still says Linda once still leaks. And it would fire on
    /// this pipeline's own output — "my daughter Chris" is a relationship and a name — so the run that reads its
    /// output back would report its own invented names as residue. Read back against the roster instead, the
    /// output is searched for Linda, which is the only question that matters.
    /// </para>
    /// </summary>
    public bool FindRelativesNamedInText { get; init; } = true;

    /// <summary>What this engine replaces with, and what it draws invented names from.</summary>
    public SiluetaLineage Lineage { get; }

    public RedactionResult Redact(string text, DeidentificationContext context, SiluetaPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);
        policy ??= SiluetaPolicy.SafeHarbor;

        // The caller's roster, plus the people this transcript names through a relationship and the roster does
        // not already know. Everything below — detection, the choice of invented names, the read-back — runs
        // against this, so a relative found here is searched for everywhere the roster is.
        (DeidentificationContext roster, int unrostered) = FindRelativesNamedInText
            ? WithRelativesNamedIn(text, context)
            : (context, 0);

        var found = new List<Detection>();
        foreach (IDetector detector in _detectors)
        {
            found.AddRange(detector.Detect(text, roster));
        }

        List<Detection> applied = Resolve(found, policy, out int ambiguous);

        // No invented name may be one this very run would detect, or the next pass over the output finds
        // the surrogate and replaces it again. The test is the detectors themselves rather than a second
        // copy of their threshold, because two copies of a rule are two rules that will disagree.
        bool WouldBeFound(string candidate) =>
            _detectors.Any(detector => detector.Detect(candidate, roster).Any());

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
        var unavailable = new SortedSet<string>(StringComparer.Ordinal);
        int cursor = 0;

        foreach (Detection detection in applied)
        {
            sb.Append(text, cursor, detection.Start - cursor);
            sb.Append(Replacement(detection, text, WouldBeFound, policy, unavailable));
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
            _detectors.SelectMany(detector => detector.Detect(redacted, roster)).ToList(),
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
            // Left at its default — which says there is none — when this build has no measurement of its own.
            MeasuredLeakRate = PublishedLeakRate.Current?.Summary ?? RedactionManifest.NoMeasurement,
            AmbiguousAttributions = ambiguous,
            UnrosteredPeople = unrostered,
            DeparturesFromSafeHarbor = [.. DeparturesFromSafeHarbor(policy)],
            RelativesRule = FindRelativesNamedInText ? RelativesInText.Fingerprint(QuasiIdentifierVocabulary.Default) : "off",
            KeptKinds = [.. Enum.GetValues<IdentifierKind>()
                .Where(kind => policy.ActionFor(kind) == RedactionAction.Keep)
                .Select(kind => kind.ToString())
                .Order(StringComparer.Ordinal)],
            SurrogatesUnavailable = [.. unavailable],
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
    /// ("Sofia Reyes" over "Sofia"). What survives is a set of spans that do not touch, in reading order.
    /// <para>
    /// Candidates that overlap are <b>united</b>, not sorted and discarded. Discarding was right for
    /// containment and wrong for everything else: with a roster holding <c>Ana Maria</c> and
    /// <c>Maria Perez</c>, the text <c>Ana Maria Perez</c> produced two candidates where neither contains
    /// the other, the longer one won, and the characters only the loser covered — a word of somebody's
    /// name — stayed in the transcript. Uniting cannot leave a character that a detector found and nothing
    /// covers, and there is a test that says exactly that.
    /// </para>
    /// <para>
    /// Who the span is about is decided separately from where it runs, because those are different
    /// questions and the honest answer to the second one is sometimes nobody. A span keeps a subject when
    /// something that covers the whole of it names that subject and nothing covering it disagrees — which
    /// is every ordinary case, including the household where a mother and daughter share a surname, since
    /// there the full name contains the surname rather than crossing it. Where two people are called the
    /// same thing, or where two names cross, no one can say whose mention it is: the span is replaced with
    /// a label instead of an invented name, coreference is lost for it, and the manifest counts it. Losing
    /// coreference on a span is a cost; attributing a sentence to the wrong person is a different kind of
    /// thing entirely.
    /// </para>
    /// <para>
    /// Every tie is broken on the candidate's own content — length, confidence, position, kind, subject —
    /// and never on the order the roster was written in. Reordering a roster used to change whose life a
    /// sentence was about.
    /// </para>
    /// </summary>
    private static List<Detection> Resolve(List<Detection> candidates, SiluetaPolicy policy) =>
        Resolve(candidates, policy, out _);

    /// <summary>How a policy differs from Safe Harbor, one line per difference, in a stable order.</summary>
    private static IEnumerable<string> DeparturesFromSafeHarbor(SiluetaPolicy policy)
    {
        SiluetaPolicy floor = SiluetaPolicy.SafeHarbor;
        if (ReferenceEquals(policy, floor))
        {
            return [];
        }

        var lines = new List<string>();
        foreach (IdentifierKind kind in Enum.GetValues<IdentifierKind>())
        {
            if (policy.ActionFor(kind) != floor.ActionFor(kind))
            {
                lines.Add($"{kind}: {policy.ActionFor(kind)} (Safe Harbor: {floor.ActionFor(kind)})");
            }
        }

        if (policy.MinConfidence != floor.MinConfidence)
        {
            lines.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"minConfidence: {policy.MinConfidence:R} (Safe Harbor: {floor.MinConfidence:R})"));
        }

        return lines.Order(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The caller's roster plus every relative the transcript names that the roster does not already find, each
    /// with no subject. Returns the caller's own context untouched when there is nobody to add.
    /// </summary>
    private (DeidentificationContext Roster, int Added) WithRelativesNamedIn(string text, DeidentificationContext context)
    {
        IReadOnlyList<NamedRelative> named = RelativesInText.NamedIn(text);
        if (named.Count == 0)
        {
            return (context, 0);
        }

        // Whether a name is already somebody's is asked of the detectors themselves, as the invented-name check
        // is, so that "already on the roster" cannot mean one thing here and another in the matcher.
        bool Known(string candidate, DeidentificationContext against) =>
            _detectors.Any(detector => detector.Detect(candidate, against).Any());

        DeidentificationContext roster = context.Copy();
        int added = 0;

        foreach (NamedRelative relative in named)
        {
            // A listed person referred to by a relationship — "her daughter Jamileth" for the Yamilet on the
            // roster — is the roster's. And a name found twice ("my daughter Linda ... my daughter Linda") is one
            // person, which the roster built so far already answers.
            if (Known(relative.GivenName, context) || Known(relative.GivenName, roster))
            {
                continue;
            }

            roster.AddValue(relative.Name, relative.Kind, string.Empty);
            if (relative.Surname is { } surname)
            {
                roster.AddValue(relative.GivenName, relative.Kind, string.Empty);

                // The surname alone only when nobody listed already carries it. "Linda Pryor" is unlisted and
                // "Pryor" is the patient: registering the unlisted surname too would put a second, subjectless
                // claim on every "Mr. Pryor" in the file.
                if (!Known(surname, context))
                {
                    roster.AddValue(surname, relative.Kind, string.Empty);
                }
            }

            added++;
        }

        return added == 0 ? (context, 0) : (roster, added);
    }

    /// <inheritdoc cref="Resolve(List{Detection}, SiluetaPolicy)"/>
    /// <param name="ambiguous">How many surviving spans lost their subject because nothing could say whose
    /// they were.</param>
    private static List<Detection> Resolve(List<Detection> candidates, SiluetaPolicy policy, out int ambiguous)
    {
        List<Detection> ordered = candidates
            .Where(d => d.Confidence >= policy.MinConfidence && policy.ActionFor(d.Kind) != RedactionAction.Keep)
            .OrderBy(d => d.Start)
            .ThenByDescending(d => d.Length)
            .ThenByDescending(d => d.Confidence)
            .ThenBy(d => (int)d.Kind)
            .ThenBy(d => d.SubjectId, StringComparer.Ordinal)
            .ThenBy(d => d.DetectorId, StringComparer.Ordinal)
            .ToList();

        ambiguous = 0;
        var accepted = new List<Detection>();

        for (int i = 0; i < ordered.Count;)
        {
            // One sweep: the component is everything reachable from here by overlap, which because the
            // list is in reading order is a run of candidates that starts before the running end.
            int start = ordered[i].Start;
            int end = ordered[i].End;
            int last = i;

            while (last + 1 < ordered.Count && ordered[last + 1].Start < end)
            {
                last++;
                end = Math.Max(end, ordered[last].End);
            }

            accepted.Add(Unite(ordered, i, last, start, end, policy, ref ambiguous));
            i = last + 1;
        }

        return accepted;
    }

    /// <summary>One component of overlapping candidates, as the single span that replaces them.</summary>
    private static Detection Unite(
        List<Detection> ordered, int from, int to, int start, int end, SiluetaPolicy policy, ref int ambiguous)
    {
        Detection anchor = ordered[from];
        for (int i = from + 1; i <= to; i++)
        {
            if (Precedes(ordered[i], anchor))
            {
                anchor = ordered[i];
            }
        }

        if (from == to)
        {
            return anchor;
        }

        // Only what covers the whole span gets a say in whose it is. A surname inside a full name is not a
        // second opinion about the mention; a second person with the same full name is. And a candidate that
        // covers the span but names nobody abstains rather than disagrees: a relative the transcript named and
        // nobody listed — "my daughter Linda Pryor", around the patient's own "Pryor" — owns the span without
        // having a subject, and that is not a conflict about whose it is. Counting it as one made every such
        // mention look like a run that had given up on somebody.
        bool covered = false;
        string? subject = null;
        bool disagreement = false;
        for (int i = from; i <= to; i++)
        {
            if (ordered[i].Start != start || ordered[i].End != end)
            {
                continue;
            }

            covered = true;
            if (ordered[i].SubjectId is not { Length: > 0 } opinion)
            {
                continue;
            }

            if (subject is null)
            {
                subject = opinion;
            }
            else if (!string.Equals(subject, opinion, StringComparison.Ordinal))
            {
                disagreement = true;
            }
        }

        if (covered && subject is null)
        {
            // Covered, and nobody covering it named anyone: the span is unattributed because it never had an
            // owner the run knew, not because an owner was lost. A label, and nothing to count.
            return new Detection(start, end - start, anchor.Kind, anchor.DetectorId, anchor.Confidence, string.Empty, anchor.Match);
        }

        if (!covered)
        {
            // Nothing covers the union: the names cross. Then the only attribution that can be trusted is
            // one every candidate in the component already agrees on.
            subject = ordered[from].SubjectId;
            for (int i = from + 1; i <= to && !disagreement; i++)
            {
                disagreement = !string.Equals(subject, ordered[i].SubjectId, StringComparison.Ordinal);
            }
        }

        if (disagreement || subject is not { Length: > 0 })
        {
            // Counted only where an invented name was actually lost: the span's kind is one the policy
            // replaces with a surrogate, and somebody in the component had a subject to lose. A phone
            // number belongs to no subject and becomes a label either way, so two shape rules meeting —
            // or a name found inside an e-mail address — is not a person nobody could name. The counter
            // said it was, and the demo page is where that showed: a transcript with three people, all
            // three of them named in the output, reporting one lost attribution.
            bool anyoneWasNamed = false;
            for (int i = from; i <= to && !anyoneWasNamed; i++)
            {
                anyoneWasNamed = ordered[i].SubjectId is { Length: > 0 };
            }

            subject = string.Empty;
            if (anyoneWasNamed && policy.ActionFor(anchor.Kind) == RedactionAction.Surrogate)
            {
                ambiguous++;
            }
        }

        return new Detection(
            start,
            end - start,
            anchor.Kind,
            anchor.DetectorId,
            anchor.Confidence,
            subject,
            anchor.Match);
    }

    /// <summary>Which of two candidates speaks for a united span: the longest, then the most confident,
    /// then the earliest — and after that, its own content, so that nothing depends on roster order.</summary>
    private static bool Precedes(Detection candidate, Detection incumbent) =>
        (candidate.Length, candidate.Confidence) != (incumbent.Length, incumbent.Confidence)
            ? candidate.Length > incumbent.Length ||
              (candidate.Length == incumbent.Length && candidate.Confidence > incumbent.Confidence)
            : (candidate.Start, (int)candidate.Kind, candidate.SubjectId, candidate.DetectorId).CompareTo(
                  (incumbent.Start, (int)incumbent.Kind, incumbent.SubjectId, incumbent.DetectorId)) < 0;

    /// <summary>The original text is read here, from the transcript the caller passed in, rather than
    /// carried on the detection: see <see cref="Detection"/> for why that matters.</summary>
    private string Replacement(
        Detection detection, string source, Func<string, bool> wouldBeFound, SiluetaPolicy policy, ISet<string> unavailable)
    {
        string original = detection.TextIn(source);

        return policy.ActionFor(detection.Kind) switch
        {
            RedactionAction.Surrogate when detection.SubjectId is { Length: > 0 } subjectId =>
                SurrogateOrLabel(detection.Kind, subjectId, original, wouldBeFound, unavailable),
            RedactionAction.YearOnly => YearOf(original),
            RedactionAction.Generalize => Generalized(detection.Kind, original),
            RedactionAction.Keep => original,
            _ => LabelFor(detection.Kind),
        };
    }

    /// <summary>
    /// An invented name of the right shape, or a label when there is no pool to draw one from.
    /// <para>
    /// The label is the honest fallback and the pool of people is not. Reaching for the only pool there
    /// is turned "Acme Corporation" into "Ariel Bravo": the identifier went, and in its place was a person
    /// who reads as a fact. The built-in lineage ships no company names at all — an invented company is
    /// very likely a real one — so for a company this is the default path, and it is recorded.
    /// </para>
    /// <para>
    /// A subject the vault already knows keeps its name even when today's lineage could not have minted
    /// it. The corpus already says that name; labelling the same company in the next document would split
    /// one subject into two presentations, which is the inconsistency the vault exists to prevent.
    /// </para>
    /// </summary>
    private string SurrogateOrLabel(
        IdentifierKind kind, string subjectId, string original, Func<string, bool> wouldBeFound, ISet<string> unavailable)
    {
        if (!Vault.TryGetSurrogate(subjectId, out _) && !Vault.Pools.Has(kind))
        {
            unavailable.Add(kind.ToString());
            return LabelFor(kind);
        }

        return Vault.Pools.Fit(Vault.SurrogateFor(subjectId, kind, wouldBeFound), Tokenizer.Tokenize(original).Count);
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
