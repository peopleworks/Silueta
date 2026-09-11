namespace Silueta.Core;

/// <summary>How a span was recognised. Kept on every detection because the provenance of each removal
/// is what an expert determination has to document — "we ran a redactor" is not a method.</summary>
public enum MatchKind
{
    /// <summary>The text matched a known value letter for letter (ignoring case and accents).</summary>
    Exact,

    /// <summary>The text sounded like a known value: the ASR wrote it differently, we heard it the same.</summary>
    Phonetic,

    /// <summary>Close enough to a known value under edit distance, above the configured threshold.</summary>
    Fuzzy,

    /// <summary>A pattern rule fired (phone, email, date…), with no known value involved.</summary>
    Pattern,
}

/// <summary>
/// One span of the transcript that Silueta believes identifies someone. Offsets are UTF-16 indices into
/// the original text, so the caller can always show exactly what was replaced.
/// </summary>
/// <param name="SubjectId">The entity this span belongs to, when it came from a known value. Surrogates
/// are chosen per subject, which is what keeps one person the same invented name all the way through.</param>
public sealed record Detection(
    int Start,
    int Length,
    IdentifierKind Kind,
    string Text,
    string DetectorId,
    double Confidence,
    string? SubjectId = null,
    MatchKind Match = MatchKind.Exact)
{
    public int End => Start + Length;

    public bool Overlaps(Detection other) => Start < other.End && other.Start < End;
}
