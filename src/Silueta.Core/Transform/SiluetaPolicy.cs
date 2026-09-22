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
    /// Surrogates beat labels because the text stays natural: whatever reads it next — a person, a model,
    /// a metric — still sees a sentence, which labels in quantity do not give you.
    /// </para>
    /// <para>
    /// <b>They do not hide a leak, and this used to claim they did.</b> The argument was that a name that
    /// slipped past the redactor would not stand out among plausible invented ones. It does not survive
    /// contact with an adversary: the pool is forty-three words in a public MIT repository, so subtracting
    /// it from the capitalised tokens leaves exactly the leaks. Run on this library's own README example,
    /// that subtraction returns "Rays" and "Ellie" — the two leaks the README names in prose. At corpus
    /// scale the pool is not even needed: twenty-one given names across hundreds of subjects makes
    /// surrogates the repeated names and leaks the singletons, and a frequency sort finds them.
    /// </para></summary>
    Surrogate,

    /// <summary>Keep the year, drop the rest. Safe Harbor allows the year and nothing finer.</summary>
    YearOnly,

    /// <summary>Widen until it stops identifying: 94 becomes "90 or older", and 85004 becomes 850XX — Safe
    /// Harbor keeps three digits of a postal code only where that area holds more than 20,000 people, and
    /// <see cref="CensusZipTable"/> is the count that says which; the rest become 000XX.</summary>
    Generalize,

    /// <summary>Leave it. For kinds a caller has decided are not identifiers in their setting — and for a
    /// state, which Safe Harbor itself keeps.</summary>
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

    /// <summary>"0.3" since the table has a row for a city and one for a state; "0.2" since it named kinds the
    /// standard does not: an organisation, a product, a client. Removing more keeps the name true; it does not
    /// keep the rules identical, and a corpus labelled 0.2 was redacted under a table that did not have those
    /// rows — its cities survived because nothing looked for them.</summary>
    public string Version { get; init; } = "0.3";

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

                // Places. A city is a subdivision smaller than a state and goes; a state is the one place the
                // standard keeps. Keep is a row here and not an absence, so the fingerprint says so — and a kept
                // reading is dropped before overlaps are resolved, so "Georgia" the patient is never kept because
                // Georgia is also a state.
                [IdentifierKind.City] = RedactionAction.Label,
                [IdentifierKind.State] = RedactionAction.Keep,

                // Not Safe Harbor identifiers, and named here anyway. ActionFor would give them Label
                // without a row, but the fingerprint walks this table only: a kind handled by the
                // fallback is handled under a rule the digest does not describe. Surrogate is the
                // action; whether a surrogate can actually be drawn depends on the lineage having a
                // pool for the kind, and the engine records it when one cannot.
                [IdentifierKind.Organization] = RedactionAction.Surrogate,
                [IdentifierKind.Product] = RedactionAction.Surrogate,
                [IdentifierKind.ClientName] = RedactionAction.Surrogate,
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
