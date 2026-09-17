using System.Text.Json;
using Silueta.Cli;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The command line as an emitter of artefacts. The engine reads its own output back and reports what it
/// still finds there; the MCP server refuses to write when that residue is not empty. The CLI computed
/// the same residue and wrote the file anyway, then printed the transcript, then complained — so
/// <c>--out</c> left a file named like a redacted transcript that still says somebody's name, and
/// whatever ran next had no way to know. One rule, two copies, and only one of them was true.
/// </summary>
public sealed class CliTests : IDisposable
{
    private static readonly string[] Family =
    [
        "Aguilar", "Bravo", "Castro", "Duarte", "Espinal", "Fuentes", "Gaitan", "Herrera",
        "Ibarra", "Jimenez", "Lara", "Medina", "Nieves", "Ochoa", "Prado", "Quintero",
        "Rivas", "Salazar", "Toledo", "Urena", "Vargas", "Zamora",
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("silueta-cli-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    private string Write(string name, string content)
    {
        string path = Path(name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Roster(string name, IEnumerable<KnownIdentifierDto> people) =>
        Write(name, JsonSerializer.Serialize(people.ToArray(), SiluetaJsonContext.Default.KnownIdentifierDtoArray));

    private static KnownIdentifierDto Person(string subjectId, string value, IdentifierKind kind) =>
        new() { SubjectId = subjectId, Value = value, Kind = kind.ToString() };

    /// <summary>A roster that leaves the vault exactly one family name to choose, so the surrogate the
    /// first record gets is the one the second record's real staff member is called.</summary>
    private static IEnumerable<KnownIdentifierDto> Forcing(string onlyFreeSurname) =>
        Family.Where(n => n != onlyFreeSurname)
            .Select((n, i) => Person($"blocker-{i}", n, IdentifierKind.OtherName));

    private int Redact(params string[] arguments) =>
        Commands.Redact(Commands.ParseOptions(arguments), _output, _error);

    /// <summary>Runs the two records of <see cref="ResidueTests"/> through the command line: the second
    /// one is the record whose invented name is a real person in its own roster.</summary>
    private string ResidueRun(string outFlag, string outPath)
    {
        string vaultPath = Path("vault.json");

        string first = Write("a.txt", "Eleanor Vasquez slept well.");
        string firstRoster = Roster("roster-a.json",
            Forcing("Urena").Append(Person("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)));

        Assert.Equal(0, Redact(
            "--in", first, "--record", "record-a", "--context", firstRoster,
            "--vault", vaultPath, "--out", Path("a.redacted.txt")));
        Assert.Contains("Urena", File.ReadAllText(Path("a.redacted.txt")), StringComparison.Ordinal);

        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();

        string second = Write("b.txt", "Eleanor Vasquez saw Rosa Urena today.");
        string secondRoster = Roster("roster-b.json",
        [
            Person("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName),
            Person("staff-2", "Rosa Urena", IdentifierKind.StaffName),
        ]);

        string[] arguments = outFlag.Length == 0
            ? ["--in", second, "--record", "record-b", "--context", secondRoster, "--vault", vaultPath,
               "--manifest", Path("b.manifest.json")]
            : ["--in", second, "--record", "record-b", "--context", secondRoster, "--vault", vaultPath,
               "--manifest", Path("b.manifest.json"), outFlag, outPath];

        Assert.Equal(3, Redact(arguments));
        return vaultPath;
    }

    [Fact]
    public void A_run_that_leaves_residue_writes_no_redacted_file()
    {
        string outPath = Path("b.redacted.txt");

        ResidueRun("--out", outPath);

        // The file used to be there, holding "Rosa Urena", named like something safe to hand on.
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void A_run_that_leaves_residue_does_not_print_the_transcript_either()
    {
        // Without --out the text goes to stdout, which is a pipe, a log, or a model's context.
        ResidueRun(string.Empty, string.Empty);

        Assert.DoesNotContain("Urena", _output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Eleanor", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_leaves_residue_still_writes_the_vault_and_the_manifest()
    {
        // Refusing to emit is not refusing to record. The vault holds assignments this run may have
        // minted, and the manifest is the only account of why the run did not hold.
        string vaultPath = ResidueRun("--out", Path("b.redacted.txt"));

        Assert.True(File.Exists(vaultPath));
        RedactionManifest manifest = JsonSerializer.Deserialize(
            File.ReadAllText(Path("b.manifest.json")), SiluetaJsonContext.Default.RedactionManifest)!;
        Assert.True(manifest.ResidualSpans > 0);
    }

    [Fact]
    public void A_clean_run_still_writes_and_prints()
    {
        string input = Write("clean.txt", "Ellenor Vasques rested well. Call 602-555-0147.");
        string roster = Roster("roster-clean.json",
            [Person("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)]);
        string outPath = Path("clean.redacted.txt");

        Assert.Equal(0, Redact("--in", input, "--record", "r-1", "--context", roster, "--out", outPath));

        Assert.True(File.Exists(outPath));
        Assert.DoesNotContain("Vasques", File.ReadAllText(outPath), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, Redact("--in", input, "--record", "r-1", "--context", roster));
        Assert.Contains("rested well", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lineage_of_your_own_decides_the_words_that_land_in_the_file()
    {
        string lineage = Write("lineage.json", """
            {
              "lineage": "clinica", "version": "2", "language": "es-MX",
              "pools": { "given": ["Ale", "Noa"], "family": ["Bravo", "Toledo"] },
              "labels": { "Phone": "[TELÉFONO]" }
            }
            """);
        string input = Write("es.txt", "Llamó al 602-555-0147 esta mañana.");
        string outPath = Path("es.redactado.txt");

        Assert.Equal(0, Redact(
            "--in", input, "--record", "r-es", "--lineage", lineage, "--out", outPath,
            "--manifest", Path("es.manifest.json")));

        Assert.Contains("[TELÉFONO]", File.ReadAllText(outPath), StringComparison.Ordinal);

        RedactionManifest manifest = JsonSerializer.Deserialize(
            File.ReadAllText(Path("es.manifest.json")), SiluetaJsonContext.Default.RedactionManifest)!;
        Assert.Equal("clinica", manifest.Lineage);
        Assert.Equal("es-MX", manifest.LineageLanguage);
        Assert.NotEmpty(manifest.LineageFingerprint);

        // And the run says which lineage it used, because an operator has to be able to see that without
        // opening the manifest.
        Assert.Contains("clinica/2", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lineage_the_loader_refuses_stops_the_run_and_says_which_rule_broke()
    {
        string lineage = Write("bad.json", """
            { "lineage": "x", "version": "1", "pools": { "given": ["Vasquez", "Vasques"], "family": ["Bravo"] } }
            """);
        string input = Write("any.txt", "Nothing here.");
        string outPath = Path("any.redactado.txt");

        Assert.Equal(2, Redact("--in", input, "--record", "r-1", "--lineage", lineage, "--out", outPath));

        Assert.False(File.Exists(outPath));
        Assert.Contains("sound alike", _error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""[ { "value": "Acme Corporation", "kind": "Organisation", "subjectId": "client-7" } ]""", "Organization")]
    [InlineData("""[ { "value": "Acme Corporation", "subjectId": "client-7" } ]""", null)]
    public void A_roster_entry_whose_kind_cannot_be_read_stops_the_run(string roster, string? suggestion)
    {
        // It used to become OtherName in silence, which after the company kinds is a company sent to the
        // pool of people's names by a typo — or by leaving the field out.
        string input = Write("call.txt", "Acme Corporation called.");
        string rosterPath = Write("roster.json", roster);
        string outPath = Path("call.redacted.txt");

        Assert.Equal(2, Redact("--in", input, "--record", "r-1", "--context", rosterPath, "--out", outPath));

        Assert.False(File.Exists(outPath));
        string error = _error.ToString();
        Assert.Contains("entry 1", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Acme", error, StringComparison.OrdinalIgnoreCase);

        if (suggestion is not null)
        {
            Assert.Contains(suggestion, error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Evaluate_prints_the_caveats_before_any_rate_and_writes_a_report_with_no_values_in_it()
    {
        string gold = System.IO.Path.Combine(McpToolDocumentationTests.RepoRoot, "corpus-synthetic", "readme-demo");
        string report = Path("report.json");

        Assert.Equal(0, Commands.Evaluate(Commands.ParseOptions(["--gold", gold, "--out", report]), _output, _error));

        string printed = _output.ToString();
        int caveat = printed.IndexOf("caveat:", StringComparison.Ordinal);
        int rate = printed.IndexOf("leak rate", StringComparison.Ordinal);
        Assert.True(caveat >= 0 && caveat < rate, "The corpus caveats have to come before the first rate.");
        Assert.Contains("95% CI", printed, StringComparison.Ordinal);
        Assert.Contains("deny-list", printed, StringComparison.Ordinal);

        string json = File.ReadAllText(report);
        Assert.Contains("\"leakRate\"", json, StringComparison.Ordinal);
        foreach (string value in (string[])["Ellie", "Rays", "Jamileth", "Vasques", "602-555-0147"])
        {
            Assert.DoesNotContain(value, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Evaluate_refuses_a_directory_that_is_not_a_corpus()
    {
        Assert.Equal(2, Commands.Evaluate(Commands.ParseOptions(["--gold", _directory]), _output, _error));
        Assert.Contains("no gold documents", _error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_record_id_is_required_and_is_not_the_file_name()
    {
        string input = Write("Ana-Perez.txt", "Nothing here.");

        Assert.Equal(2, Redact("--in", input));
        Assert.DoesNotContain("Ana-Perez", _output.ToString(), StringComparison.Ordinal);
    }
}
