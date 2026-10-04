using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Silueta.Core;

namespace Silueta.ContinuityAudit;

/// <summary>A gold span an attributed replacement overlaps: where, and what the adjudicated gold calls it.</summary>
public sealed record GoldOverlap(int Start, int Length, IdentifierKind Kind);

/// <summary>
/// One replacement the engine gave to a subject's invented name, as offsets and ids. No text: the person who
/// confirms it reads the private assessor sheet, and this record is the part that may travel.
/// </summary>
public sealed record Attribution(
    string DocumentId,
    int Start,
    int Length,
    string SubjectId,
    IdentifierKind Kind,
    MatchKind Match,
    IReadOnlyList<GoldOverlap> Gold,
    IReadOnlyList<string> Flags);

/// <summary>One roster subject across the run: how many records named it on their roster, how many invented
/// names it had after those records, and how many were retired. It holds when there was one and none retired.</summary>
public sealed record SubjectContinuity(string SubjectId, int Documents, int InventedNames, int Retired, bool Holds);

public sealed record AuditReport(
    string CoreSha256,
    int Documents,
    bool ContinuityHolds,
    IReadOnlyList<SubjectContinuity> Subjects,
    IReadOnlyList<Attribution> Attributions,
    int Flagged,
    IReadOnlyList<string> Caveats);

/// <summary>A document and what the engine did to it. Kept for the assessor sheet; never serialised.</summary>
public sealed record DocumentRun(GoldDocument Document, RedactionResult Redaction);

/// <summary>
/// Gate C2 of the 1.0 acceptance criteria (<c>tools/blind-evaluation/ACCEPTANCE.md</c>): nobody is given
/// somebody else's name.
/// <para>
/// Half of it is a fact the vault can be asked: after each record, does every subject still have one invented
/// name, and was any retired? The other half is not in the data. The gold says a span is a staff name; it
/// does not say whose. A rule that decided from the text which person a span names would be a second matcher,
/// and it would disagree with the first exactly where the question matters. So every replacement the engine
/// attributed to a person is listed — all of them, not a sample — with mechanical flags that direct attention,
/// and a person confirms the list.
/// </para>
/// <para>
/// The run is the one <c>silueta evaluate</c> scores for C1: one vault across the corpus, the default lineage,
/// the loader's document order. ContinuityAuditTests holds the two to the same scores, so the gates cannot end
/// up describing two different engines.
/// </para>
/// </summary>
public static class Auditor
{
    /// <summary>The roster gives the subject one kind and the gold calls this span another.</summary>
    public const string KindDiffers = "kind-differs";

    /// <summary>The engine put a person's invented name on text the gold says identifies nobody.</summary>
    public const string NoGoldSpan = "no-gold-span";

    /// <summary>One replacement covers more than one gold span: possibly two people under one name.</summary>
    public const string SeveralGoldSpans = "several-gold-spans";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The SHA-256 of the Silueta.Core this process actually loaded, which is what the audit is of.</summary>
    public static string CoreAssemblySha256()
    {
        using FileStream stream = File.OpenRead(typeof(SiluetaEngine).Assembly.Location);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static (AuditReport Report, IReadOnlyList<DocumentRun> Runs) Run(GoldCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        // The evaluator's silueta configuration: known values plus the lineage's pattern pack, one vault shared
        // across the corpus. FromLineage builds exactly that; the equivalence is pinned by test, not assumed.
        SiluetaLineage lineage = SiluetaLineage.Default;
        var vault = new PseudonymVault(lineage.Pools);
        SiluetaEngine engine = SiluetaEngine.FromLineage(lineage, vault);

        var runs = new List<DocumentRun>();
        var documents = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var attributions = new List<Attribution>();

        foreach (GoldDocument document in corpus.Documents)
        {
            RedactionResult redaction = engine.Redact(document.Text, document.ToContext());
            runs.Add(new DocumentRun(document, redaction));

            foreach (string subject in document.Roster.Select(entry => entry.SubjectId).Distinct(StringComparer.Ordinal))
            {
                documents[subject] = documents.GetValueOrDefault(subject) + 1;
                HashSet<string> seen = names.TryGetValue(subject, out HashSet<string>? existing)
                    ? existing
                    : names[subject] = new HashSet<string>(StringComparer.Ordinal);
                if (vault.TryGetSurrogate(subject, out string name))
                {
                    seen.Add(name);
                }
            }

            // An ambiguous span is labelled, not given anybody's name, and carries no subject: nothing to confirm.
            foreach (Detection applied in redaction.Applied
                .Where(d => d.SubjectId is { Length: > 0 } && d.Kind.IsPersonName())
                .OrderBy(d => d.Start))
            {
                List<GoldOverlap> gold = [.. document.Spans
                    .Where(span => span.Start < applied.End && applied.Start < span.End)
                    .Select(span => new GoldOverlap(span.Start, span.Length, span.Kind))];

                var flags = new List<string>();
                if (gold.Count == 0) flags.Add(NoGoldSpan);
                if (gold.Count > 1) flags.Add(SeveralGoldSpans);
                if (gold.Any(span => span.Kind != applied.Kind)) flags.Add(KindDiffers);

                attributions.Add(new Attribution(document.DocumentId, applied.Start, applied.Length,
                    applied.SubjectId!, applied.Kind, applied.Match, gold, flags));
            }
        }

        List<SubjectContinuity> subjects = [.. documents.Keys.Order(StringComparer.Ordinal).Select(subject =>
        {
            int retired = vault.RetiredSurrogatesFor(subject).Count;
            int inventedNames = names[subject].Count;
            return new SubjectContinuity(subject, documents[subject], inventedNames, retired, inventedNames <= 1 && retired == 0);
        })];

        var report = new AuditReport(
            CoreAssemblySha256(),
            runs.Count,
            subjects.All(s => s.Holds),
            subjects,
            attributions,
            attributions.Count(a => a.Flags.Count > 0),
            [
                "Continuity is checked automatically. Attribution is not: C2 passes only when a person has confirmed, " +
                "over this complete list, that no replacement put one person's invented name on another person.",
                "Flags direct attention and are not failures: an annotator's role label can differ from the roster's.",
                "Subject ids are the gold's opaque ids. Invented names and text are left out of this report; the " +
                "assessor sheet holds them and is private.",
            ]);

        return (report, runs);
    }

    public static string ToJson(AuditReport report) => JsonSerializer.Serialize(report, Json);

    /// <summary>
    /// What a person needs to confirm each attribution: the replaced text in its sentence, whose invented name
    /// it was given, and what the gold says is there. It quotes the records, so it stays where they are.
    /// </summary>
    public static string AssessorSheet(AuditReport report, IReadOnlyList<DocumentRun> runs)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(runs);
        Dictionary<string, GoldDocument> byId = runs.ToDictionary(r => r.Document.DocumentId, r => r.Document, StringComparer.Ordinal);

        var sheet = new StringBuilder();
        sheet.AppendLine("# C2 assessor sheet — PRIVATE: quotes the records");
        sheet.AppendLine();
        sheet.AppendLine($"Core SHA-256 `{report.CoreSha256}`, {report.Documents} documents.");
        sheet.AppendLine($"Continuity: **{(report.ContinuityHolds ? "holds" : "BROKEN")}**.");
        foreach (SubjectContinuity broken in report.Subjects.Where(s => !s.Holds))
        {
            sheet.AppendLine($"- `{broken.SubjectId}`: {broken.InventedNames} invented names over {broken.Documents} records, {broken.Retired} retired.");
        }
        sheet.AppendLine();
        sheet.AppendLine($"{report.Attributions.Count} attributions, {report.Flagged} flagged. For each, mark whether the replaced");
        sheet.AppendLine("text names the person on the right. C2 fails on any \"no\" where it names a different person.");
        sheet.AppendLine();

        foreach (Attribution a in report.Attributions)
        {
            GoldDocument document = byId[a.DocumentId];
            string text = document.Text;
            string subject = string.Join(" / ", document.Roster
                .Where(entry => entry.SubjectId == a.SubjectId)
                .Select(entry => $"{entry.Value} ({entry.Kind})")
                .Distinct(StringComparer.Ordinal));
            int from = Math.Max(0, a.Start - 40);
            int to = Math.Min(text.Length, a.Start + a.Length + 40);
            string context = $"…{text[from..a.Start]}**{text[a.Start..(a.Start + a.Length)]}**{text[(a.Start + a.Length)..to]}…"
                .ReplaceLineEndings(" ");
            string gold = a.Gold.Count == 0
                ? "none"
                : string.Join("; ", a.Gold.Select(g => $"\"{text.Substring(g.Start, g.Length)}\" {g.Kind}"));
            string flags = a.Flags.Count == 0 ? "" : $" — flags: {string.Join(", ", a.Flags)}";

            sheet.AppendLine($"- [ ] `{a.DocumentId}` [{a.Start},{a.Start + a.Length}) {a.Match}{flags}");
            sheet.AppendLine($"  - text: {context}");
            sheet.AppendLine($"  - given the invented name of: `{a.SubjectId}` = {subject}");
            sheet.AppendLine($"  - gold: {gold}");
        }

        return sheet.ToString();
    }

    public static int Execute(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }
            options[args[i][2..]] = args[i + 1];
        }

        if (!options.TryGetValue("gold", out string? gold) || !options.TryGetValue("core-sha256", out string? expected) ||
            !options.TryGetValue("out", out string? reportPath))
        {
            error.WriteLine(
                "silueta-continuity-audit --gold <adjudicated gold dir> --core-sha256 <frozen Core SHA-256> " +
                "--out <report.json> [--assessor-sheet <private sheet.md>]");
            return 2;
        }

        // The gate is about the frozen engine. Main moves on; an audit of whatever happened to be built would be
        // a check of nothing in particular, so it does not run at all.
        string actual = CoreAssemblySha256();
        if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine(
                $"This process loaded Silueta.Core {actual}, not the frozen engine {expected.Trim()}. Copy the Core " +
                "assembly from the frozen CLI package over this tool's Silueta.Core.dll (see the README). Nothing was written.");
            return 2;
        }

        (AuditReport report, IReadOnlyList<DocumentRun> runs) = Run(GoldCorpus.Load(gold));
        File.WriteAllText(reportPath, ToJson(report));
        output.WriteLine($"Wrote {reportPath}.");
        if (options.TryGetValue("assessor-sheet", out string? sheetPath))
        {
            File.WriteAllText(sheetPath, AssessorSheet(report, runs));
            output.WriteLine($"Wrote {sheetPath} — it quotes the records; keep it with them.");
        }

        output.WriteLine(
            $"Continuity {(report.ContinuityHolds ? "holds" : "is BROKEN")} over {report.Subjects.Count} subjects. " +
            $"{report.Attributions.Count} attributions listed, {report.Flagged} flagged: C2 also needs a person to " +
            "confirm the list.");
        return report.ContinuityHolds ? 0 : 1;
    }
}
