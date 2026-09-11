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
/// </summary>
public sealed class SiluetaPolicy
{
    public string Name { get; init; } = "safe-harbor";

    public string Version { get; init; } = "0.1";

    /// <summary>Fixed so two runs of the same corpus produce the same invented names. Change it and the
    /// same person becomes someone else, which is occasionally what you want and usually a bug.</summary>
    public int SurrogateSeed { get; init; } = 1917;

    /// <summary>Detections below this are dropped. Raising it trades leaks for readability; the number
    /// belongs to the agency, and the harness is how they pick it.</summary>
    public double MinConfidence { get; init; } = 0.7;

    public Dictionary<IdentifierKind, RedactionAction> Actions { get; init; } = new()
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
    };

    /// <summary>The default: everything Safe Harbor names, removed or widened.</summary>
    public static SiluetaPolicy SafeHarbor { get; } = new();

    public RedactionAction ActionFor(IdentifierKind kind) =>
        Actions.TryGetValue(kind, out RedactionAction action) ? action : RedactionAction.Label;
}
