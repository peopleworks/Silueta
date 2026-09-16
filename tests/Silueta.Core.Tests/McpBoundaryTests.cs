using Silueta.Core;
using Silueta.Mcp.Tools;

namespace Silueta.Core.Tests;

/// <summary>
/// The MCP server is a privacy boundary, not a wrapper. Its tools decide what reaches a model, and a
/// model's context is the one place nothing can be taken back from.
/// <para>
/// Every test here is a way the server handed over something the repository's own README, the server's
/// README and SKILL.md all promise it never will. The worst of them: <c>redact_transcript</c> reads any
/// path the model writes, and a file that matches no roster comes back unchanged — so pointing it at the
/// vault returned the whole subject-to-invented-name table, marked safe to export. Three documents say
/// "there is no re-identification tool and there will not be one"; that promise was kept by not
/// <em>declaring</em> one while the capability sat inside another tool's file read.
/// </para>
/// </summary>
public sealed class McpBoundaryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("silueta-mcp-").FullName;
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable(RedactionTools.RootVariable);

    public McpBoundaryTests() => Environment.SetEnvironmentVariable(RedactionTools.RootVariable, _root);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RedactionTools.RootVariable, _previousRoot);
        Directory.Delete(_root, recursive: true);
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Roster() => Write("roster.json", """
        [ { "value": "Eleanor Vasquez", "kind": "PatientName", "subjectId": "patient-1" } ]
        """);

    [Fact]
    public void The_lineage_is_the_operators_decision_and_it_is_read_from_the_environment()
    {
        // Not a tool parameter: which dictionaries a corpus is redacted with is decided by whoever set
        // the server up. A model that can choose the word lists can choose a lineage whose "labels"
        // leave everything where it is, and the manifest would still say the run succeeded.
        string lineage = Write("lineage.json", """
            {
              "lineage": "clinica", "version": "1", "language": "es-MX",
              "pools": { "given": ["Ale", "Noa"], "family": ["Bravo", "Toledo"] },
              "labels": { "Phone": "[TELÉFONO]" }
            }
            """);
        Environment.SetEnvironmentVariable(RedactionTools.LineageVariable, lineage);

        try
        {
            RedactionReport report = RedactionTools.RedactText("Llamó al 602-555-0147.", "r-1");

            Assert.Contains("[TELÉFONO]", report.RedactedText!, StringComparison.Ordinal);
            Assert.StartsWith("clinica/1", report.Lineage, StringComparison.Ordinal);
            Assert.NotEmpty(report.LineageFingerprint);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RedactionTools.LineageVariable, null);
        }
    }

    [Fact]
    public void A_vault_pointed_at_by_the_lineage_variable_is_refused_like_any_other_vault()
    {
        var vault = new PseudonymVault().Assign("patient-1", "Ale Espinal");
        string vaultPath = Write("vault.json", vault.ToJson());
        Environment.SetEnvironmentVariable(RedactionTools.LineageVariable, vaultPath);

        try
        {
            Exception thrown = Assert.ThrowsAny<Exception>(() =>
                RedactionTools.RedactText("Nothing here.", "r-1"));

            Assert.Contains("vault", thrown.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ale Espinal", thrown.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RedactionTools.LineageVariable, null);
        }
    }

    [Fact]
    public void A_lineage_outside_the_root_is_refused_like_any_other_path()
    {
        Environment.SetEnvironmentVariable(
            RedactionTools.LineageVariable, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "elsewhere.json"));

        try
        {
            Assert.ThrowsAny<Exception>(() => RedactionTools.RedactText("Nothing here.", "r-1"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RedactionTools.LineageVariable, null);
        }
    }

    [Fact]
    public void An_inline_roster_entry_with_a_kind_that_cannot_be_read_is_refused_without_repeating_it()
    {
        // "Value|Kind|SubjectId" is easy to write with two columns swapped, which puts a person's name in
        // the kind. So the refusal names the position and the closest real kind, and never the input.
        Exception thrown = Assert.ThrowsAny<Exception>(() =>
            RedactionTools.RedactText("Acme Corporation called.", "r-1", ["Acme Corporation|Organisation|client-7"]));

        Assert.Contains("entry 1", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Organization", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Organisation", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme", thrown.Message, StringComparison.OrdinalIgnoreCase);

        Exception swapped = Assert.ThrowsAny<Exception>(() =>
            RedactionTools.RedactText("Eleanor Vasquez rested.", "r-1", ["PatientName|Eleanor Vasquez|patient-1"]));
        Assert.DoesNotContain("Eleanor", swapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_roster_file_entry_with_a_kind_that_cannot_be_read_is_refused()
    {
        string transcript = Write("call.txt", "Acme Corporation called.");
        string roster = Write("roster.json", """
            [ { "value": "Acme Corporation", "kind": "Client Company", "subjectId": "client-7" } ]
            """);

        Exception thrown = Assert.ThrowsAny<Exception>(() =>
            RedactionTools.RedactTranscript(transcript, "r-1", roster));

        Assert.Contains("entry 1", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Acme", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_vault_can_never_be_read_back_through_the_redaction_tool()
    {
        var vault = new PseudonymVault().Assign("patient-1", "Ale Espinal");
        string vaultPath = Write("vault.json", vault.ToJson());

        Exception thrown = Assert.ThrowsAny<Exception>(() =>
            RedactionTools.RedactTranscript(vaultPath, "r-1"));

        Assert.Contains("vault", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ale Espinal", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_file_read_without_a_roster_is_not_handed_back()
    {
        // Without a roster no name can be found, so what comes back is the transcript with a phone
        // number masked — the file, essentially, handed to the model that deliberately did not open it.
        // Guarding on "nothing was replaced" was not enough: the pattern pack fires on the phone, the
        // date and the age, so the count was three and every name survived.
        string transcript = Write("notes.txt", "Eleanor Vasquez rested well. Call 602-555-0147.");

        RedactionReport report = RedactionTools.RedactTranscript(transcript, "r-2");

        Assert.True(report.SpansReplaced > 0);
        Assert.Null(report.RedactedText);
        Assert.Contains("no roster", report.Withheld ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_roster_that_matched_nothing_is_not_handed_back_either()
    {
        string transcript = Write("other.txt", "The patient rested well and ate breakfast.");

        RedactionReport report = RedactionTools.RedactTranscript(transcript, "r-2b", Roster());

        Assert.Equal(0, report.SpansReplaced);
        Assert.Null(report.RedactedText);
        Assert.Contains("nothing was replaced", report.Withheld ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_transcript_that_was_really_redacted_does_come_back()
    {
        string transcript = Write("shift.txt", "Ellenor Vasques rested well.");

        RedactionReport report = RedactionTools.RedactTranscript(transcript, "r-3", Roster());

        Assert.True(report.SpansReplaced > 0);
        Assert.NotNull(report.RedactedText);
        Assert.DoesNotContain("Ellenor", report.RedactedText!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(report.Withheld);
    }

    [Fact]
    public void The_output_path_may_not_be_the_vault()
    {
        string transcript = Write("shift2.txt", "Ellenor Vasques rested well.");
        string vaultPath = Path.Combine(_root, "v2.json");

        Assert.ThrowsAny<Exception>(() =>
            RedactionTools.RedactTranscript(transcript, "r-4", Roster(), vaultPath, outputPath: vaultPath));
    }

    [Fact]
    public void Reading_outside_the_configured_root_is_refused()
    {
        string outside = Path.Combine(Path.GetTempPath(), $"silueta-outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "Eleanor Vasquez rested well.");
        try
        {
            Exception thrown = Assert.ThrowsAny<Exception>(() => RedactionTools.RedactTranscript(outside, "r-5"));
            Assert.Contains(RedactionTools.RootVariable, thrown.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Text_that_still_contains_identifiers_is_never_returned()
    {
        // The one case the library itself calls "this still names someone" is the one case where the
        // text used to come back anyway, with "DO NOT EXPORT THIS TEXT" printed underneath it.
        var vault = new PseudonymVault();
        string vaultPath = Path.Combine(_root, "v3.json");

        // Record A mints a surrogate ending in a surname that record B's roster also holds.
        string blockers = string.Join(",\n", new[]
        {
            "Aguilar", "Bravo", "Castro", "Duarte", "Espinal", "Fuentes", "Gaitan", "Herrera", "Ibarra",
            "Jimenez", "Lara", "Medina", "Nieves", "Ochoa", "Prado", "Quintero", "Rivas", "Salazar",
            "Toledo", "Vargas", "Zamora",
        }.Select((n, i) => $$"""{ "value": "{{n}}", "kind": "OtherName", "subjectId": "b{{i}}" }"""));

        string rosterA = Write("rosterA.json",
            $"[ {{ \"value\": \"Eleanor Vasquez\", \"kind\": \"PatientName\", \"subjectId\": \"patient-1\" }},\n{blockers} ]");
        string first = Write("a.txt", "Eleanor Vasquez slept well.");
        RedactionReport a = RedactionTools.RedactTranscript(first, "r-a", rosterA, vaultPath);

        Assert.NotNull(a.RedactedText);
        Assert.Contains("Urena", a.RedactedText!, StringComparison.Ordinal);

        string rosterB = Write("rosterB.json", """
            [ { "value": "Eleanor Vasquez", "kind": "PatientName", "subjectId": "patient-1" },
              { "value": "Rosa Urena",      "kind": "StaffName",   "subjectId": "staff-2"  } ]
            """);
        string second = Write("b.txt", "Eleanor Vasquez saw Rosa Urena today.");
        RedactionReport b = RedactionTools.RedactTranscript(second, "r-b", rosterB, vaultPath);

        Assert.True(b.ResidualSpans > 0);
        Assert.False(b.SafeToExport);
        Assert.Null(b.RedactedText);
        Assert.Contains("still finds", b.Withheld ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        _ = vault;
    }

    [Fact]
    public void A_record_id_that_names_someone_on_the_roster_is_refused()
    {
        string transcript = Write("shift3.txt", "Ellenor Vasques rested well.");

        Exception thrown = Assert.ThrowsAny<Exception>(() =>
            RedactionTools.RedactTranscript(transcript, "Eleanor Vasquez", Roster()));

        Assert.Contains("opaque", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }
}
