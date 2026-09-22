namespace Silueta.Core;

/// <summary>A value the caller already knows identifies someone in this record.</summary>
public sealed record KnownIdentifier(string Value, IdentifierKind Kind, string SubjectId);

/// <summary>
/// What the caller knows about the record before reading a word of it: who the patient is, who lives in
/// the house, which nurse took the shift, the phone number on file.
/// <para>
/// This is the difference between Silueta and an open-ended entity recogniser. An agency knows its own
/// people; matching values you already hold is far more accurate than guessing which capitalised word is
/// a name, and it almost never removes a clinical term by mistake. The recogniser's job is only the
/// residue — the neighbour, the doctor mentioned once, the name nobody wrote down.
/// </para>
/// </summary>
public sealed class DeidentificationContext
{
    private readonly List<KnownIdentifier> _known = new();

    /// <param name="recordId">An opaque id for this record. It is required, and it must be opaque: this
    /// value travels in the manifest, which is the artefact that leaves with the corpus. A caller that
    /// passes a file name, a patient name or an account number has published an identifier through the
    /// one file whose whole purpose is to prove none were published.</param>
    public DeidentificationContext(string recordId)
    {
        ArgumentNullException.ThrowIfNull(recordId);
        if (string.IsNullOrWhiteSpace(recordId))
        {
            throw new ArgumentException("A record needs an opaque id of its own.", nameof(recordId));
        }

        RecordId = recordId;
    }

    /// <summary>Opaque id of the record being processed. Appears in the manifest, never in the output.</summary>
    public string RecordId { get; }

    public IReadOnlyList<KnownIdentifier> Known => _known;

    /// <summary>
    /// The same record and roster, as a new context the engine can add to without touching the caller's. The
    /// relatives a transcript names are added to the roster of one run; they are not the caller's to keep.
    /// </summary>
    internal DeidentificationContext Copy()
    {
        var copy = new DeidentificationContext(RecordId);
        copy._known.AddRange(_known);
        return copy;
    }

    public DeidentificationContext AddValue(string value, IdentifierKind kind, string subjectId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _known.Add(new KnownIdentifier(value.Trim(), kind, subjectId));
        }

        return this;
    }

    /// <summary>
    /// Adds a subject by full name — and, when the subject is a person, each part of the name on its own.
    /// <para>
    /// Half the mentions in a real transcript are a first name alone ("Sofia said she'd call"), and the
    /// other half are the full name. Registering both, under one subject id, is what lets the same person
    /// get the same surrogate whichever way they were said. Parts shorter than three characters are
    /// skipped: "de", "la" and initials would match half the transcript.
    /// </para>
    /// <para>
    /// A company is registered whole and only whole. Its words are common nouns: split "Acme Corporation"
    /// and "Corporation" becomes that client, so every unrelated corporation in the transcript is
    /// replaced — measured, "bought a corporation" came back as "bought a Guadalupe". The price is real
    /// and is paid on purpose: "Acme" said alone is no longer found, because it only ever was by accident
    /// of the wrong rule. Put the short form on the roster as a value of its own. Whether a kind is a
    /// person is decided in one place, <see cref="IdentifierKindExtensions.IsPersonName"/>, and not by
    /// the caller choosing between this method and <see cref="AddValue"/>.
    /// </para>
    /// </summary>
    public DeidentificationContext AddPerson(string subjectId, string fullName, IdentifierKind kind)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return this;
        }

        AddValue(fullName, kind, subjectId);

        if (!kind.IsPersonName())
        {
            return this;
        }

        foreach (Token part in Tokenizer.Tokenize(fullName))
        {
            if (part.Text.Length >= 3)
            {
                AddValue(part.Text, kind, subjectId);
            }
        }

        return this;
    }
}
