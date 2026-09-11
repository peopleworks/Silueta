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

    public DeidentificationContext(string recordId) => RecordId = recordId;

    /// <summary>Opaque id of the record being processed. Appears in the manifest, never in the output.</summary>
    public string RecordId { get; }

    public IReadOnlyList<KnownIdentifier> Known => _known;

    public DeidentificationContext AddValue(string value, IdentifierKind kind, string subjectId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _known.Add(new KnownIdentifier(value.Trim(), kind, subjectId));
        }

        return this;
    }

    /// <summary>
    /// Adds a person by full name, and also each part of it on its own.
    /// <para>
    /// Half the mentions in a real transcript are a first name alone ("Sofia said she'd call"), and the
    /// other half are the full name. Registering both, under one subject id, is what lets the same person
    /// get the same surrogate whichever way they were said. Parts shorter than three characters are
    /// skipped: "de", "la" and initials would match half the transcript.
    /// </para>
    /// </summary>
    public DeidentificationContext AddPerson(string subjectId, string fullName, IdentifierKind kind)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return this;
        }

        AddValue(fullName, kind, subjectId);

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
