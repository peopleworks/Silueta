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
/// the original text, so the caller can always show exactly what was replaced — by slicing the text they
/// already hold.
/// <para>
/// <b>This type deliberately does not carry the matched text.</b> It used to, and that made the result of
/// a redaction a printable, serialisable record of every identifier in the transcript: the one object a
/// caller is most likely to log, return from an API or attach to a ticket was a list of the names we had
/// just been asked to remove. Offsets plus a length say everything an audit needs and nothing an attacker
/// wants. Whoever legitimately needs the value has the original text in hand; whoever does not, should
/// not receive it by accident.
/// </para>
/// </summary>
/// <param name="SubjectId">The entity this span belongs to, when it came from a known value. Surrogates
/// are chosen per subject, which is what keeps one person the same invented name all the way through.</param>
public sealed record Detection(
    int Start,
    int Length,
    IdentifierKind Kind,
    string DetectorId,
    double Confidence,
    string? SubjectId = null,
    MatchKind Match = MatchKind.Exact)
{
    public int End => Start + Length;

    public bool Overlaps(Detection other) => Start < other.End && other.Start < End;

    /// <summary>
    /// The text this span covers, read from the transcript the caller already has. Explicit by design:
    /// obtaining an identifier is a thing a reader can see in the code and grep for, not a property that
    /// rides along into every log line.
    /// </summary>
    public string TextIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Start >= 0 && End <= source.Length ? source[Start..End] : string.Empty;
    }
}
