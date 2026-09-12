using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The vault as a file on disk. It is the only artefact that can undo a redaction and there is no second
/// copy of it by design, so every failure here is unrecoverable by construction.
/// </summary>
public sealed class VaultFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("silueta-vaultfile-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "foo": 1 }""")]
    [InlineData("""{ "subjects": {} }""")]
    public void A_json_file_that_is_not_a_vault_is_not_read_as_an_empty_one(string json)
    {
        // The version field defaulted to "2" when absent, so any JSON object passed the version check
        // and produced a valid, empty vault. Paired with an atomic writer, that is a loader which cannot
        // tell an empty vault from the wrong file: one mistyped --vault re-minted every surrogate and
        // destroyed whatever the file actually was.
        Assert.Throws<InvalidOperationException>(() => PseudonymVault.FromJson(json));
    }

    [Fact]
    public void A_real_vault_still_round_trips()
    {
        var vault = new PseudonymVault().Assign("patient-1", "Ale Espinal");

        var reloaded = PseudonymVault.FromJson(vault.ToJson());

        Assert.Equal("Ale Espinal", reloaded.SurrogateFor("patient-1"));
    }

    [Fact]
    public void Saving_over_a_file_that_is_not_a_vault_is_refused()
    {
        string path = Path("notes.txt");
        File.WriteAllText(path, "Eleanor Vasquez rested well.");

        Assert.ThrowsAny<Exception>(() => new PseudonymVault().Assign("p", "Ale Espinal").SaveTo(path));
        Assert.Equal("Eleanor Vasquez rested well.", File.ReadAllText(path));
    }

    [Fact]
    public void Loading_a_file_that_is_not_a_vault_is_refused_rather_than_silently_empty()
    {
        string path = Path("roster.json");
        File.WriteAllText(path, """[ { "value": "Eleanor Vasquez" } ]""");

        Assert.ThrowsAny<Exception>(() => PseudonymVault.LoadOrCreate(path));
    }

    [Fact]
    public void A_blocked_subject_can_be_given_a_new_name_without_losing_the_old_one()
    {
        // A surrogate cleared for one record can collide with a later record's roster. The vault keeps
        // the assignment on purpose, so that record's residue never clears and it is blocked on every
        // rerun — the only escape was hand-editing the vault, which every document forbids.
        var vault = new PseudonymVault().Assign("patient-1", "Ale Espinal");

        string replacement = vault.Remint("patient-1", candidate => candidate.Contains("Ale", StringComparison.Ordinal));

        Assert.NotEqual("Ale Espinal", replacement);
        Assert.Equal(replacement, vault.SurrogateFor("patient-1"));

        // The retired name stays claimed: a corpus redacted before the remint still says "Ale Espinal",
        // and handing that name to somebody else would merge two people across the two halves.
        Assert.Contains("Ale Espinal", vault.RetiredSurrogatesFor("patient-1"));
        Assert.NotEqual("Ale Espinal", vault.SurrogateFor("patient-2"));
    }

    [Fact]
    public void A_remint_survives_a_round_trip_through_the_file()
    {
        var vault = new PseudonymVault().Assign("patient-1", "Ale Espinal");
        string replacement = vault.Remint("patient-1", candidate => candidate.Contains("Ale", StringComparison.Ordinal));

        var reloaded = PseudonymVault.FromJson(vault.ToJson());

        Assert.Equal(replacement, reloaded.SurrogateFor("patient-1"));
        Assert.Contains("Ale Espinal", reloaded.RetiredSurrogatesFor("patient-1"));
    }

    [Fact]
    public void The_vault_can_be_entered_from_the_side_the_holder_actually_has()
    {
        // The redacted transcript contains invented names, never SIL- codes. TryReidentify takes a code,
        // so the only way in was through the one thing the corpus does not contain — and the only
        // surrogate-shaped query minted a new name as a side effect of being asked.
        var vault = new PseudonymVault().Assign("patient-1", "Ale Espinal");
        int before = vault.Count;

        Assert.True(vault.TryFindSubjectBySurrogate("Ale Espinal", out string subjectId));
        Assert.Equal("patient-1", subjectId);
        Assert.True(vault.TryGetSurrogate("patient-1", out string surrogate));
        Assert.Equal("Ale Espinal", surrogate);

        Assert.False(vault.TryGetSurrogate("nobody", out _));
        Assert.False(vault.TryFindSubjectBySurrogate("Cruz Medina", out _));
        Assert.Equal(before, vault.Count);
    }

    [Fact]
    public void A_retired_name_still_leads_back_to_its_subject()
    {
        // A corpus redacted before a remint says the old name. Someone holding that corpus has to be
        // able to get back, or the remint quietly orphaned every document that came before it.
        var vault = new PseudonymVault().Assign("patient-1", "Ale Espinal");
        vault.Remint("patient-1", candidate => candidate.Contains("Ale", StringComparison.Ordinal));

        Assert.True(vault.TryFindSubjectBySurrogate("Ale Espinal", out string subjectId));
        Assert.Equal("patient-1", subjectId);
    }
}
