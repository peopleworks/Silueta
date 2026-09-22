namespace Silueta.Core;

/// <summary>
/// What a detected span is. The list started from the eighteen HIPAA Safe Harbor identifiers
/// (45 CFR § 164.514(b)(2)) rather than a generic PII taxonomy, because the policy that decides what
/// happens to a span is written in those terms — and because a reviewer checking the work will be
/// reading the rule, not our vocabulary.
/// <para>
/// <see cref="Organization"/>, <see cref="Product"/> and <see cref="ClientName"/> are <em>not</em> Safe Harbor
/// identifiers, and neither is <see cref="State"/>, which the standard keeps. A company's transcripts are full of identifiers the
/// standard never mentions. Removing more than it asks keeps a policy named after it true — keeping
/// something it names would not — so a manifest's policy name says which standard the run meets, and
/// its per-kind counts say what was actually removed. Those are not the same list.
/// </para>
/// <para>
/// Appended rather than slotted in beside the other names, so that no existing value changes its number.
/// </para>
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

    /// <summary>
    /// A company, institution or agency named in the record: an employer, an insurer, a vendor, a client
    /// company. Not a person, so a roster entry is matched whole and never split into its words —
    /// "Acme Corporation" must not make every "corporation" in the text an identifier.
    /// <para>
    /// An employer's name <em>is</em> a Safe Harbor identifier when it is the individual's employer
    /// (identifier A names "employers"); a treating facility's generally is not. This kind does not tell
    /// the two apart, and removes both.
    /// </para>
    /// </summary>
    Organization,

    /// <summary>A product, service or brand. Not a person.</summary>
    Product,

    /// <summary>
    /// The customer of whoever holds the corpus, when that customer is a <b>person</b>.
    /// <para>
    /// This is deliberately a person and not a company. In home care — where this library started — the
    /// client is the patient, and a kind that read "client" as a company would send a patient's name to
    /// a pool of invented company names. A client that is a company is an <see cref="Organization"/>.
    /// </para>
    /// </summary>
    ClientName,

    /// <summary>
    /// A city, town or other place smaller than a state. Safe Harbor removes "all geographic subdivisions
    /// smaller than a state" (45 CFR § 164.514(b)(2)(i)(B)); an organisation's own policy may keep it, because a
    /// city is statistics as long as nobody knows whose it is.
    /// </summary>
    City,

    /// <summary>
    /// A state of the United States, the District of Columbia or Puerto Rico. Safe Harbor <em>keeps</em> it — the
    /// one place the standard allows — so it is found in order to be counted, and removed only by a policy
    /// stricter than Safe Harbor.
    /// </summary>
    State,
}

/// <summary>
/// The one place that says which kinds are people.
/// <para>
/// Two things turn on the answer: whether a roster entry is registered part by part (a person is often
/// called by a first name alone; a company's words are common nouns), and which word lists an invented
/// name is drawn from. If the roster and the vault each kept their own list, a kind added to one and not
/// the other would be a company in one place and a person in the next.
/// </para>
/// </summary>
public static class IdentifierKindExtensions
{
    public static bool IsPersonName(this IdentifierKind kind) => kind is
        IdentifierKind.PatientName or
        IdentifierKind.FamilyName or
        IdentifierKind.StaffName or
        IdentifierKind.OtherName or
        IdentifierKind.ClientName;

    /// <summary>
    /// Reads a kind the way a person writes one — by its name, in any case — and nothing else.
    /// <para>
    /// Not <c>Enum.TryParse</c>, which is a parser for enum <em>values</em> and reads far more than names:
    /// "3" as the third kind, "99" as a kind that does not exist, and "PatientName, Phone" as both at
    /// once. Every file this library reads a kind from — a roster, a lineage, a pattern pack — was written
    /// by someone, and each of those readings is a way for a typo to become a rule.
    /// </para>
    /// </summary>
    public static bool TryParseName(string? text, out IdentifierKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        foreach (IdentifierKind candidate in Enum.GetValues<IdentifierKind>())
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The kind a misspelling most likely meant, or null when nothing is close.
    /// <para>
    /// For an error message, and it is the only thing an error message may say about what was written: it
    /// answers with one of the kinds' own names, never with the input. A roster with two columns swapped
    /// puts a person's name where the kind goes, and an error that repeated the field would print that
    /// name. The threshold is high on purpose — "Organisation" is close to "Organization"; a name is close
    /// to nothing.
    /// </para>
    /// </summary>
    public static string? ClosestName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string folded = Folding.StripAccents(text.Trim()).ToLowerInvariant();
        (string? best, double score) = (null, 0.8);

        foreach (IdentifierKind candidate in Enum.GetValues<IdentifierKind>())
        {
            string name = candidate.ToString();
            double ratio = Similarity.Ratio(folded, name.ToLowerInvariant());
            if (ratio >= score)
            {
                (best, score) = (name, ratio);
            }
        }

        return best;
    }

    /// <summary>
    /// What to tell a caller whose roster entry has a kind that cannot be read. By position, with the
    /// closest real kind when there is one — the same words from the command line and the MCP server,
    /// because two copies of an error message drift into two different rules about what it may reveal.
    /// </summary>
    public static string UnreadableKindMessage(int entryNumber, string? text)
    {
        string hint = ClosestName(text) is { } closest ? $" The closest kind is {closest}." : string.Empty;
        string missing = string.IsNullOrWhiteSpace(text) ? "has no \"kind\"" : "has a \"kind\" this build does not know";

        return
            $"Roster entry {entryNumber} {missing}.{hint} It used to be read as OtherName without a word, which " +
            "sends a company to the pool of people's names. Kinds: " +
            string.Join(", ", Enum.GetNames<IdentifierKind>()) + ".";
    }
}
