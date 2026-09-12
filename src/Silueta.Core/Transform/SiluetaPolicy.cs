using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Silueta.Core;

/// <summary>What to do with a span once it has been found.</summary>
public enum RedactionAction
{
    /// <summary>Replace with a bracketed label: <c>[PATIENT]</c>. Honest, and unreadable in quantity.</summary>
    Label,

    /// <summary>Replace with a consistent invented name.
    /// <para>
    /// Surrogates beat labels for two reasons. The text stays natural, so whatever reads it next — a
    /// person, a model, a metric — still sees a sentence. And a name that slips through the redactor no
    /// longer stands out among the labels: with everything else replaced by plausible names, a leak is
    /// not advertised to whoever is skimming.
    /// </para></summary>
    Surrogate,

    /// <summary>Keep the year, drop the rest. Safe Harbor allows the year and nothing finer.</summary>
    YearOnly,

    /// <summary>Widen until it stops identifying: 94 becomes "90 or older", a ZIP keeps three digits.</summary>
    Generalize,

    /// <summary>Leave it. Only ever for kinds a caller has decided are not identifiers in their setting.</summary>
    Keep,
}

/// <summary>
/// The rules of one run: what happens to each kind of identifier, and how sure a detector has to be
/// before it is believed. Policies are data, and the manifest records which one ran — a de-identified
/// corpus whose policy is unknown cannot be defended later.
/// <para>
/// A policy is immutable once built, and that is a privacy property rather than a style preference. The
/// alternative, which this type used to be, is a shared mutable default: one consumer writes
/// <c>SafeHarbor.Actions[Phone] = Keep</c>, every engine built afterwards hands back phone numbers, and
/// every manifest still says the policy was <c>safe-harbor/0.1</c>. The name would be intact and the
/// behaviour would be gone.
/// </para>
/// </summary>
public sealed class SiluetaPolicy
{
    private readonly FrozenDictionary<IdentifierKind, RedactionAction> _actions = Defaults.Actions;
    private string? _fingerprint;

    public string Name { get; init; } = "safe-harbor";

    public string Version { get; init; } = "0.1";

    /// <summary>Detections below this are dropped. Raising it trades leaks for readability; the number
    /// belongs to the agency, and the harness is how they pick it.</summary>
    public double MinConfidence { get; init; } = 0.7;

    /// <summary>
    /// What happens to each kind. Read-only by construction: the setter takes a frozen copy, so neither
    /// the caller's dictionary nor this one can be edited after the policy exists.
    /// </summary>
    public IReadOnlyDictionary<IdentifierKind, RedactionAction> Actions
    {
        get => _actions;
        init => _actions = value is null or { Count: 0 }
            ? Defaults.Actions
            : value.ToFrozenDictionary();
    }

    /// <summary>
    /// A short digest of everything that changes what a run does: the actions, the confidence floor, and
    /// the policy's own name and version. Invented names are not in here because they are no longer a
    /// function of the policy — the vault mints and remembers them, and the vault is the artefact that
    /// records which name went to whom.
    /// <para>
    /// This exists because two corpora can be labelled <c>safe-harbor/0.1</c> and have been redacted
    /// under different rules — someone edited a copy, or a later version of this library changed a
    /// default. The name is what a human writes down; the fingerprint is what actually ran, and it is
    /// the field to compare when two manifests disagree.
    /// </para>
    /// </summary>
    public string Fingerprint => _fingerprint ??= ComputeFingerprint();

    /// <summary>The default: everything Safe Harbor names, removed or widened.</summary>
    public static SiluetaPolicy SafeHarbor { get; } = new();

    public RedactionAction ActionFor(IdentifierKind kind) =>
        _actions.TryGetValue(kind, out RedactionAction action) ? action : RedactionAction.Label;

    /// <summary>
    /// The Safe Harbor table, in a nested class on purpose. An instance field initialiser here reads a
    /// static one, and static initialisers run in declaration order: written as a plain static member of
    /// this class, <see cref="SafeHarbor"/> would be constructed against a null table depending on where
    /// the line sat in the file. A nested type initialises on first touch instead, so the order of the
    /// lines above stops mattering.
    /// </summary>
    private static class Defaults
    {
        internal static readonly FrozenDictionary<IdentifierKind, RedactionAction> Actions =
            new Dictionary<IdentifierKind, RedactionAction>
            {
                [IdentifierKind.PatientName] = RedactionAction.Surrogate,
                [IdentifierKind.FamilyName] = RedactionAction.Surrogate,
                [IdentifierKind.StaffName] = RedactionAction.Surrogate,
                [IdentifierKind.OtherName] = RedactionAction.Surrogate,
                [IdentifierKind.Phone] = RedactionAction.Label,
                [IdentifierKind.Email] = RedactionAction.Label,
                [IdentifierKind.Url] = RedactionAction.Label,
                [IdentifierKind.IpAddress] = RedactionAction.Label,
                [IdentifierKind.Address] = RedactionAction.Label,
                [IdentifierKind.PostalCode] = RedactionAction.Generalize,
                [IdentifierKind.Date] = RedactionAction.YearOnly,
                [IdentifierKind.AgeOver89] = RedactionAction.Generalize,
                [IdentifierKind.RecordNumber] = RedactionAction.Label,
                [IdentifierKind.AccountNumber] = RedactionAction.Label,
                [IdentifierKind.DeviceId] = RedactionAction.Label,
                [IdentifierKind.Other] = RedactionAction.Label,
            }.ToFrozenDictionary();
    }

    /// <summary>
    /// Built from a canonical text rather than from <see cref="object.GetHashCode"/>: the digest goes
    /// into a manifest that has to still mean the same thing on another machine, in another process, in
    /// two years. Enum names and invariant formatting, sorted, so the order a dictionary happens to
    /// enumerate in never reaches the hash.
    /// </summary>
    private string ComputeFingerprint()
    {
        var canonical = new StringBuilder();
        canonical.Append("silueta-policy/1\n")
            .Append(Name).Append('\n')
            .Append(Version).Append('\n')
            .Append(MinConfidence.ToString("R", CultureInfo.InvariantCulture)).Append('\n');

        foreach ((IdentifierKind kind, RedactionAction action) in _actions
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            canonical.Append(kind).Append('=').Append(action).Append('\n');
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(digest.AsSpan(0, 8));
    }
}
