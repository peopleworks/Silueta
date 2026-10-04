using System.Text.Json;
using Silueta.ContinuityAudit;

namespace Silueta.Core.Tests;

/// <summary>
/// Gate C2 of the 1.0 acceptance criteria (<c>tools/blind-evaluation/ACCEPTANCE.md</c>): nobody is given
/// somebody else's name. Continuity is a fact the vault can be asked about; who a span refers to is not in the
/// gold and not in the text, so the audit lists every attribution for a person to confirm. These tests hold
/// the audit to auditing the same run <c>silueta evaluate</c> scores, and to keeping text out of the report
/// that is meant to travel.
/// </summary>
public sealed class ContinuityAuditTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("silueta-c2-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Gold => Path.Combine(_directory, "gold");

    /// <summary>One gold document. Spans are given as (quote, occurrence, kind) so offsets are found, not typed.</summary>
    private void Document(string id, string text, (string Value, string Kind, string Subject)[] roster,
        params (string Quote, int Occurrence, string Kind)[] spans)
    {
        Directory.CreateDirectory(Gold);
        var located = spans.Select(s =>
        {
            int start = -1;
            for (int i = 0; i <= s.Occurrence; i++) start = text.IndexOf(s.Quote, start + 1, StringComparison.Ordinal);
            Assert.True(start >= 0, $"'{s.Quote}' #{s.Occurrence} is not in the fixture text.");
            return new { start, length = s.Quote.Length, kind = s.Kind, annotator = "fixture" };
        });
        var file = new
        {
            documentId = id, source = "synthetic-test", language = "en", text, recordedOn = "2026-10-02",
            roster = roster.Select(r => new { value = r.Value, kind = r.Kind, subjectId = r.Subject }),
            spans = located,
        };
        File.WriteAllText(Path.Combine(Gold, id + ".json"), JsonSerializer.Serialize(file));
    }

    [Fact]
    public void A_subject_mentioned_in_two_records_keeps_one_invented_name()
    {
        (string, string, string)[] roster = [("Maribel Quintana", "PatientName", "person-01")];
        Document("visit-a", "Maribel Quintana took her pills. Maribel slept well.", roster,
            ("Maribel Quintana", 0, "PatientName"), ("Maribel", 1, "PatientName"));
        Document("visit-b", "Second visit. Maribel Quintana asked for water.", roster,
            ("Maribel Quintana", 0, "PatientName"));

        (AuditReport report, _) = Auditor.Run(GoldCorpus.Load(Gold));

        Assert.True(report.ContinuityHolds);
        SubjectContinuity subject = Assert.Single(report.Subjects);
        Assert.Equal(("person-01", 2, 1, 0, true),
            (subject.SubjectId, subject.Documents, subject.InventedNames, subject.Retired, subject.Holds));
        Assert.Equal(3, report.Attributions.Count);
        Assert.All(report.Attributions, a => Assert.Empty(a.Flags));
    }

    [Fact]
    public void A_staff_member_given_the_patients_invented_name_is_listed_and_flagged()
    {
        Document("shared-first-name", "Linda Ruiz rested. Nurse Linda changed the dressing.",
            [("Linda Ruiz", "PatientName", "person-01")],
            ("Linda Ruiz", 0, "PatientName"), ("Linda", 1, "StaffName"));

        (AuditReport report, _) = Auditor.Run(GoldCorpus.Load(Gold));

        int nurse = "Linda Ruiz rested. Nurse ".Length;
        Attribution flagged = Assert.Single(report.Attributions, a => a.Start == nurse);
        Assert.Equal("person-01", flagged.SubjectId);
        Assert.Contains(Auditor.KindDiffers, flagged.Flags);
        Assert.Equal(1, report.Flagged);
    }

    [Fact]
    public void The_report_carries_offsets_and_ids_and_only_the_private_sheet_carries_text()
    {
        Document("shared-first-name", "Linda Ruiz rested. Nurse Linda changed the dressing.",
            [("Linda Ruiz", "PatientName", "person-01")],
            ("Linda Ruiz", 0, "PatientName"), ("Linda", 1, "StaffName"));

        (AuditReport report, IReadOnlyList<DocumentRun> runs) = Auditor.Run(GoldCorpus.Load(Gold));
        string json = Auditor.ToJson(report);
        string sheet = Auditor.AssessorSheet(report, runs);

        Assert.DoesNotContain("Linda", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Ruiz", json, StringComparison.Ordinal);
        string redacted = runs.Single().Redaction.Text;
        foreach (string word in redacted.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsUpper(w[0])))
        {
            // An invented name in the report would file the way back beside the subject id.
            Assert.DoesNotContain(word.TrimEnd('.'), json, StringComparison.Ordinal);
        }
        Assert.Contains("Nurse Linda", sheet, StringComparison.Ordinal);
        Assert.Contains(Auditor.KindDiffers, sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_against_bits_that_are_not_the_frozen_engine_writes_nothing()
    {
        Document("only", "Nothing identifying here.", []);
        string report = Path.Combine(_directory, "report.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exit = Auditor.Execute(["--gold", Gold, "--core-sha256", new string('0', 64), "--out", report], output, error);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(report));
        Assert.Contains("frozen", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_operator_gets_input_roster_and_the_runs_output_but_no_gold()
    {
        Document("shared-first-name", "Linda Ruiz rested. Nurse Linda changed the dressing.",
            [("Linda Ruiz", "PatientName", "person-01")],
            ("Linda Ruiz", 0, "PatientName"), ("Linda", 1, "StaffName"));
        string packet = Path.Combine(_directory, "operator");

        // Invented names are drawn at random, so only the run that wrote the packet can be compared with it.
        (AuditReport report, IReadOnlyList<DocumentRun> runs) = Auditor.Run(GoldCorpus.Load(Gold));
        Auditor.WriteOperatorPacket(packet, report, runs);
        string output = runs.Single().Redaction.Text;
        // The operator corrects the very text C2 audited, starting from an untouched copy of it.
        Assert.Equal(output, File.ReadAllText(Path.Combine(packet, "final", "shared-first-name.txt")));
        Assert.Equal(output, File.ReadAllText(Path.Combine(packet, "engine-output", "shared-first-name.txt")));
        string documents = File.ReadAllText(Path.Combine(packet, "documents.md"));
        Assert.Contains("Nurse Linda changed the dressing.", documents, StringComparison.Ordinal);
        Assert.Contains("Linda Ruiz", documents, StringComparison.Ordinal);
        string everything = string.Concat(Directory.EnumerateFiles(packet, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        // The gold called the nurse StaffName; the roster never did. Nothing in the packet may carry that.
        Assert.DoesNotContain("StaffName", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("annotator", everything, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(packet, "times.json")));
    }

    [Fact]
    public void An_operator_packet_is_never_written_over_an_existing_one()
    {
        Document("only", "Nothing identifying here.", []);
        string packet = Path.Combine(_directory, "operator");
        Directory.CreateDirectory(packet);
        File.WriteAllText(Path.Combine(packet, "keep.txt"), "an operator's work");
        string report = Path.Combine(_directory, "c2.json");

        int exit = Auditor.Execute(["--gold", Gold, "--core-sha256", Auditor.CoreAssemblySha256(), "--out", report,
            "--operator-packet", packet], new StringWriter(), new StringWriter());

        Assert.Equal(2, exit);
        Assert.False(File.Exists(report));
        Assert.Equal("an operator's work", File.ReadAllText(Path.Combine(packet, "keep.txt")));
    }

    [Fact]
    public void The_audit_runs_exactly_what_silueta_evaluate_scores()
    {
        // C1 and C2 have to describe one configuration. Invented names are drawn at random, so two runs differ in
        // which names they drew and in nothing the scores see. If the evaluator's silueta configuration ever gains
        // a detector or a different vault, this fails here instead of letting the two gates describe two engines.
        GoldCorpus corpus = GoldCorpus.Load(Path.Combine(Repo.Root, "corpus-synthetic", "tts-asr"));

        (_, IReadOnlyList<DocumentRun> runs) = Auditor.Run(corpus);
        ConfigurationResult evaluated = Evaluation.Run(corpus, EvaluationConfiguration.Silueta).Configurations.Single();

        Assert.Equal(evaluated.Documents.Count, runs.Count);
        for (int i = 0; i < runs.Count; i++)
        {
            GoldDocument document = runs[i].Document;
            DeidScore audited = LeakRate.Score(document.Text, runs[i].Redaction.Text,
                document.Spans.Select(span => span.ToDetection()), runs[i].Redaction.Applied);
            DeidScore scored = evaluated.Documents[i].Score;
            Assert.Equal(evaluated.Documents[i].DocumentId, document.DocumentId);
            Assert.Equal(
                (scored.SensitiveCharacters, scored.CoveredCharacters, scored.OverRedactedCharacters, scored.SurvivingSpans),
                (audited.SensitiveCharacters, audited.CoveredCharacters, audited.OverRedactedCharacters, audited.SurvivingSpans));
        }
    }
}
