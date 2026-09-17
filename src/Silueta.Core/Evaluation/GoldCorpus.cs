using System.Text.Json;

namespace Silueta.Core;

/// <summary>One annotation: a span of a gold document, the kind of identifier it is, and who marked it.</summary>
public sealed record GoldSpan(int Start, int Length, IdentifierKind Kind, string Annotator)
{
    public int End => Start + Length;

    /// <summary>As the scorer takes it.</summary>
    public Detection ToDetection() => new(Start, Length, Kind, $"gold:{Annotator}", 1.0);
}

/// <summary>A roster entry of a gold document: who the document is about, as the organisation writes them.</summary>
public sealed record GoldRosterEntry(string Value, IdentifierKind Kind, string SubjectId);

/// <summary>
/// One transcript with its truth: the text, the people it is about, and every identifier the annotators
/// marked in it.
/// </summary>
/// <param name="Source">Where the text came from. Reports break the number down by it, because mixing a
/// transcript a recogniser damaged with one a person typed and printing one number hides exactly what the
/// number is for.</param>
public sealed record GoldDocument(
    string DocumentId,
    string Source,
    string? Language,
    string? Speaker,
    string Text,
    IReadOnlyList<GoldRosterEntry> Roster,
    IReadOnlyList<GoldSpan> Spans)
{
    /// <summary>The distinct annotators of this document, in a stable order.</summary>
    public IReadOnlyList<string> Annotators =>
        [.. Spans.Select(span => span.Annotator).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The roster as the engine takes it. The record id is the document id, which is not a name.</summary>
    public DeidentificationContext ToContext()
    {
        var context = new DeidentificationContext(DocumentId);
        foreach (GoldRosterEntry entry in Roster)
        {
            context.AddPerson(entry.SubjectId, entry.Value, entry.Kind);
        }

        return context;
    }
}

/// <summary>
/// A directory of gold documents, one JSON file each, loaded strictly.
/// <para>
/// This is the yardstick, and a yardstick that loads a bad file quietly measures with a bent rule: a span
/// past the end of the text, a kind nobody can read, an annotation with no annotator, two documents with
/// one id. Each of those produces a number. So each is refused, by file and position — never by quoting
/// the text, because this loader will one day read a corpus that is under an NDA.
/// </para>
/// </summary>
public sealed class GoldCorpus
{
    private GoldCorpus(IReadOnlyList<GoldDocument> documents) => Documents = documents;

    public IReadOnlyList<GoldDocument> Documents { get; }

    public static GoldCorpus Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException("There is no gold corpus directory at that path.");
        }

        var documents = new List<GoldDocument>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string where = Path.GetRelativePath(directory, path);
            GoldDocument document = Parse(File.ReadAllText(path), where);

            if (!ids.Add(document.DocumentId))
            {
                throw new InvalidOperationException(
                    $"{where}: two documents share the id '{document.DocumentId}'. A result filed under an id " +
                    "that means two documents cannot be traced to either.");
            }

            documents.Add(document);
        }

        if (documents.Count == 0)
        {
            throw new InvalidOperationException(
                "There are no gold documents in that directory. A corpus of nothing has no leak rate, not a leak rate of zero.");
        }

        return new GoldCorpus(documents);
    }

    private static GoldDocument Parse(string json, string where)
    {
        GoldDocumentFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, SiluetaJsonContext.Default.GoldDocumentFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{where}: not a gold document ({ex.Message}).", ex);
        }

        if (file is null || string.IsNullOrWhiteSpace(file.DocumentId))
        {
            throw new InvalidOperationException($"{where}: has no documentId.");
        }

        string id = file.DocumentId.Trim();

        if (string.IsNullOrWhiteSpace(file.Source))
        {
            throw new InvalidOperationException(
                $"{where} ({id}): has no source. Every document has to say where its text came from, or a report " +
                "cannot keep a transcript a recogniser damaged apart from one somebody typed.");
        }

        if (file.Text is null)
        {
            throw new InvalidOperationException($"{where} ({id}): has no text.");
        }

        var roster = new List<GoldRosterEntry>();
        List<KnownIdentifierDto> rosterEntries = file.Roster ?? [];
        for (int i = 0; i < rosterEntries.Count; i++)
        {
            KnownIdentifierDto entry = rosterEntries[i];
            if (string.IsNullOrWhiteSpace(entry.SubjectId))
            {
                throw new InvalidOperationException($"{where} ({id}): roster entry {i + 1} has no subjectId.");
            }

            if (!IdentifierKindExtensions.TryParseName(entry.Kind, out IdentifierKind kind))
            {
                throw new InvalidOperationException($"{where} ({id}): {IdentifierKindExtensions.UnreadableKindMessage(i + 1, entry.Kind)}");
            }

            roster.Add(new GoldRosterEntry(entry.Value, kind, entry.SubjectId.Trim()));
        }

        var spans = new List<GoldSpan>();
        List<GoldSpanFile> spanEntries = file.Spans ?? [];
        for (int i = 0; i < spanEntries.Count; i++)
        {
            GoldSpanFile span = spanEntries[i];

            if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > file.Text.Length)
            {
                throw new InvalidOperationException(
                    $"{where} ({id}): span {i + 1} lies outside the text. An annotation that points past the end " +
                    "is scored as a sensitive character nobody can cover.");
            }

            if (!IdentifierKindExtensions.TryParseName(span.Kind, out IdentifierKind kind))
            {
                string hint = IdentifierKindExtensions.ClosestName(span.Kind) is { } closest ? $" The closest kind is {closest}." : string.Empty;
                throw new InvalidOperationException($"{where} ({id}): span {i + 1} has a kind this build does not know.{hint}");
            }

            if (string.IsNullOrWhiteSpace(span.Annotator))
            {
                throw new InvalidOperationException(
                    $"{where} ({id}): span {i + 1} has no annotator. Agreement between annotators is part of what " +
                    "the number means, and an unattributed span cannot be counted towards it.");
            }

            spans.Add(new GoldSpan(span.Start, span.Length, kind, span.Annotator.Trim()));
        }

        return new GoldDocument(
            id,
            file.Source.Trim(),
            string.IsNullOrWhiteSpace(file.Language) ? null : file.Language.Trim(),
            string.IsNullOrWhiteSpace(file.Speaker) ? null : file.Speaker.Trim(),
            file.Text,
            roster,
            spans);
    }
}

/// <summary>
/// How much two annotators agree, beyond what chance would give them.
/// <para>
/// A corpus annotated by one person measures that person as much as the redactor. Cohen's kappa, over the
/// characters of the document, for one kind of identifier: 1 is complete agreement, 0 is what two
/// annotators marking at random with the same rates would reach, and below 0 is worse than that.
/// </para>
/// </summary>
public static class Agreement
{
    /// <summary>
    /// Kappa between the first two annotators of a document, for one kind.
    /// </summary>
    /// <returns>Null when the document has fewer than two annotators. Not computable is not the same as
    /// perfect, and no second annotator is invented to make it computable.</returns>
    public static double? Kappa(GoldDocument document, IdentifierKind kind)
    {
        ArgumentNullException.ThrowIfNull(document);

        IReadOnlyList<string> annotators = document.Annotators;
        if (annotators.Count < 2 || document.Text.Length == 0)
        {
            return null;
        }

        bool[] a = Marks(document, annotators[0], kind);
        bool[] b = Marks(document, annotators[1], kind);

        int n = a.Length;
        int agree = 0;
        int aMarked = 0;
        int bMarked = 0;
        for (int i = 0; i < n; i++)
        {
            agree += a[i] == b[i] ? 1 : 0;
            aMarked += a[i] ? 1 : 0;
            bMarked += b[i] ? 1 : 0;
        }

        double observed = (double)agree / n;
        double pa = (double)aMarked / n;
        double pb = (double)bMarked / n;
        double expected = (pa * pb) + ((1 - pa) * (1 - pb));

        return expected >= 1.0 ? (observed >= 1.0 ? 1.0 : 0.0) : (observed - expected) / (1 - expected);
    }

    private static bool[] Marks(GoldDocument document, string annotator, IdentifierKind kind)
    {
        bool[] marks = new bool[document.Text.Length];
        foreach (GoldSpan span in document.Spans.Where(s => s.Kind == kind && s.Annotator == annotator))
        {
            for (int i = span.Start; i < span.End; i++)
            {
                marks[i] = true;
            }
        }

        return marks;
    }
}
