namespace Silueta.Core;

/// <summary>
/// What a detected span is. The list follows the eighteen HIPAA Safe Harbor identifiers
/// (45 CFR § 164.514(b)(2)) rather than a generic PII taxonomy, because the policy that decides what
/// happens to a span is written in those terms — and because a reviewer checking the work will be
/// reading the rule, not our vocabulary.
/// </summary>
public enum IdentifierKind
{
    /// <summary>Something identifying that does not fit a named category.</summary>
    Other = 0,

    /// <summary>The person the record is about.</summary>
    PatientName,

    /// <summary>A relative or household member. Safe Harbor covers these too, which is the rule most
    /// pipelines forget: the daughter's name in a visit note is an identifier.</summary>
    FamilyName,

    /// <summary>A nurse, aide or physician. Removed in the analysis lane, kept in the operational one.</summary>
    StaffName,

    /// <summary>A person named in passing whose role is unknown.</summary>
    OtherName,

    Phone,
    Email,
    Url,
    IpAddress,
    Address,
    PostalCode,

    /// <summary>Any element of a date except the year.</summary>
    Date,

    /// <summary>An age above 89, which Safe Harbor groups into "90 or older".</summary>
    AgeOver89,

    RecordNumber,
    AccountNumber,
    DeviceId,
}
