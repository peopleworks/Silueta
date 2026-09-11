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
}

/// <summary>The de-identified text, what was replaced, and the manifest of the run.</summary>
public sealed record RedactionResult(string Text, IReadOnlyList<Detection> Applied, RedactionManifest Manifest);

/// <summary>
/// The pipeline: detect, resolve overlaps, replace under policy, and write down what happened.
/// </summary>
public sealed partial class SiluetaEngine
{
    private readonly List<IDetector> _detectors;

    public SiluetaEngine(IEnumerable<IDetector> detectors, PseudonymVault? vault = null)
    {
        _detectors = detectors.ToList();
        Vault = vault ?? new PseudonymVault();
    }

    /// <summary>Known values plus the core pattern pack: everything deterministic, nothing to download.</summary>
    public static SiluetaEngine CreateDefault() =>
        new([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()]);

    public PseudonymVault Vault { get; }

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

        var sb = new StringBuilder(text.Length);
        var subjects = new HashSet<string>(StringComparer.Ordinal);
        int cursor = 0;

        foreach (Detection detection in applied)
        {
            sb.Append(text, cursor, detection.Start - cursor);
            sb.Append(Replacement(detection, policy));
            cursor = detection.End;

            if (detection.SubjectId is { Length: > 0 } subjectId && subjects.Add(subjectId))
            {
                // Minting here keeps the vault complete: every person the text mentioned has an id,
                // whether or not the caller also has them in a structured field.
                Vault.PseudonymFor(subjectId);
            }
        }

        sb.Append(text, cursor, text.Length - cursor);

        var manifest = new RedactionManifest
        {
            RecordId = context.RecordId,
            Policy = policy.Name,
            PolicyVersion = policy.Version,
            EngineVersion = typeof(SiluetaEngine).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            RunUtc = DateTimeOffset.UtcNow,
            TextLength = text.Length,
            Subjects = subjects.Count,
        };

        foreach (Detection detection in applied)
        {
            Increment(manifest.ByKind, detection.Kind.ToString());
            Increment(manifest.ByDetector, detection.DetectorId);
            Increment(manifest.ByMatch, detection.Match.ToString());
        }

        return new RedactionResult(sb.ToString(), applied, manifest);
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

    private string Replacement(Detection detection, SiluetaPolicy policy) => policy.ActionFor(detection.Kind) switch
    {
        RedactionAction.Surrogate when detection.SubjectId is { Length: > 0 } subjectId =>
            Surrogates.ForSubject(policy.SurrogateSeed, subjectId, Tokenizer.Tokenize(detection.Text).Count),
        RedactionAction.YearOnly => YearOf(detection.Text),
        RedactionAction.Generalize => Generalized(detection),
        RedactionAction.Keep => detection.Text,
        _ => LabelFor(detection.Kind),
    };

    /// <summary>Safe Harbor keeps the year and nothing finer. A date with no year loses everything.</summary>
    private static string YearOf(string text)
    {
        Match match = YearPattern().Match(text);
        return match.Success ? match.Value : LabelFor(IdentifierKind.Date);
    }

    private static string Generalized(Detection detection) => detection.Kind switch
    {
        IdentifierKind.AgeOver89 => "90 or older",
        IdentifierKind.PostalCode when detection.Text.Length >= 5 => detection.Text[..3] + "XX",
        _ => LabelFor(detection.Kind),
    };

    private static string LabelFor(IdentifierKind kind) => kind switch
    {
        IdentifierKind.PatientName => "[PATIENT]",
        IdentifierKind.FamilyName => "[FAMILY]",
        IdentifierKind.StaffName => "[STAFF]",
        IdentifierKind.OtherName => "[NAME]",
        IdentifierKind.Phone => "[PHONE]",
        IdentifierKind.Email => "[EMAIL]",
        IdentifierKind.Url => "[URL]",
        IdentifierKind.IpAddress => "[IP]",
        IdentifierKind.Address => "[ADDRESS]",
        IdentifierKind.PostalCode => "[ZIP]",
        IdentifierKind.Date => "[DATE]",
        IdentifierKind.AgeOver89 => "[AGE 90+]",
        IdentifierKind.RecordNumber => "[RECORD]",
        IdentifierKind.AccountNumber => "[ACCOUNT]",
        IdentifierKind.DeviceId => "[DEVICE]",
        _ => "[REMOVED]",
    };

    private static void Increment(Dictionary<string, int> counter, string key) =>
        counter[key] = counter.TryGetValue(key, out int n) ? n + 1 : 1;

    [GeneratedRegex(@"\b(1[5-9]\d{2}|20\d{2})\b")]
    private static partial Regex YearPattern();
}
