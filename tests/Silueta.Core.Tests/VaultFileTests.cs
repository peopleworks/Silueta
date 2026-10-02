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

    [Fact]
    public void A_nonempty_file_at_the_lock_path_is_preserved_and_refused()
    {
        string path = Path("nonempty-lock.json");
        File.WriteAllText(path + ".lock", "existing private data");
        Assert.Throws<VaultWriteConflictException>(() => new PseudonymVault().SaveTo(path));
        Assert.False(File.Exists(path));
        Assert.Equal("existing private data", File.ReadAllText(path + ".lock"));
    }

    [Fact]
    public void A_busy_writer_fails_without_touching_the_vault_and_the_lock_can_be_reused()
    {
        string path = Path("busy.json");
        var vault = new PseudonymVault().Assign("s-1", "Ale Bravo");
        vault.SaveTo(path);
        string committed = File.ReadAllText(path);
        vault.Assign("s-2", "Noa Toledo");
        using (var held = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            VaultWriteConflictException conflict = Assert.Throws<VaultWriteConflictException>(() => vault.SaveTo(path));
            Assert.Null(conflict.InnerException);
            Assert.DoesNotContain(path, conflict.ToString(), StringComparison.Ordinal);
            Assert.Equal(committed, File.ReadAllText(path));
        }
        vault.SaveTo(path);
        Assert.Equal(2, PseudonymVault.LoadOrCreate(path).Count);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Equal(0, new FileInfo(path + ".lock").Length);
    }

    [Fact]
    public void A_directory_is_refused_as_a_vault_without_leaving_temporary_files()
    {
        string path = Path("directory.json");
        Directory.CreateDirectory(path);
        Exception failure = Assert.ThrowsAny<Exception>(() => new PseudonymVault().Assign("s-1", "Ale Bravo").SaveTo(path));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        using var unlocked = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void Sequential_additions_and_remints_keep_all_codes_and_retired_names()
    {
        string path = Path("sequence.json");
        var pools = new SurrogatePools(["Ale", "Noa", "Sol"], ["Bravo"]);
        var first = new PseudonymVault(pools).Assign("s-1", "Ale Bravo");
        string code = first.PseudonymFor("s-1");
        first.SaveTo(path);
        PseudonymVault second = PseudonymVault.LoadOrCreate(path, pools);
        second.Assign("s-2", "Sol Bravo");
        second.Remint("s-1", static _ => false);
        second.SaveTo(path);
        second.SaveTo(path); // an unchanged save also preserves every assignment
        PseudonymVault loaded = PseudonymVault.LoadOrCreate(path, pools);
        Assert.Equal(code, loaded.PseudonymFor("s-1"));
        Assert.Equal("Noa Bravo", loaded.SurrogateFor("s-1"));
        Assert.True(loaded.TryFindSubjectBySurrogate("Ale Bravo", out string subject));
        Assert.Equal("s-1", subject);
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void A_file_with_only_a_code_and_no_surrogate_remains_readable()
    {
        PseudonymVault vault = PseudonymVault.FromJson("""{ "version": "2", "subjects": { "s-1": { "pseudonym": "SIL-fixture" } } }""");
        Assert.Equal("SIL-fixture", vault.PseudonymFor("s-1"));
        Assert.False(vault.TryGetSurrogate("s-1", out _));
    }

    [Fact]
    public void Invalid_json_diagnostics_do_not_echo_private_keys_or_keep_an_inner_exception()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PseudonymVault.FromJson("""{ "version": "2", "subjects": { "PRIVATE-SUBJECT": { "pseudonym": [] } } }"""));
        Assert.DoesNotContain("PRIVATE-SUBJECT", exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void A_stale_writer_cannot_erase_another_runs_subject()
    {
        string path = Path("shared.json");
        new PseudonymVault().Assign("s-1", "Ale Bravo").SaveTo(path);
        PseudonymVault first = PseudonymVault.LoadOrCreate(path);
        PseudonymVault stale = PseudonymVault.LoadOrCreate(path);
        first.Assign("s-2", "Noa Toledo").SaveTo(path);
        string committed = File.ReadAllText(path);

        stale.Assign("s-3", "Sol Castro");
        Assert.ThrowsAny<InvalidOperationException>(() => stale.SaveTo(path));
        Assert.Equal(committed, File.ReadAllText(path));
        Assert.Equal(2, PseudonymVault.LoadOrCreate(path).Count);
    }

    [Fact]
    public void Two_writers_starting_without_a_file_cannot_replace_each_others_vault()
    {
        string path = Path("new-shared.json");
        PseudonymVault first = PseudonymVault.LoadOrCreate(path);
        PseudonymVault stale = PseudonymVault.LoadOrCreate(path);
        first.Assign("s-1", "Ale Bravo").SaveTo(path);
        string committed = File.ReadAllText(path);

        stale.Assign("s-2", "Noa Toledo");
        Assert.ThrowsAny<InvalidOperationException>(() => stale.SaveTo(path));
        Assert.Equal(committed, File.ReadAllText(path));
    }

    [Fact]
    public void A_stale_remint_cannot_orphan_the_latest_surrogate()
    {
        string path = Path("remint.json");
        var pools = new SurrogatePools(["Ale", "Noa", "Sol"], ["Bravo"]);
        new PseudonymVault(pools).Assign("s-1", "Ale Bravo").SaveTo(path);
        PseudonymVault first = PseudonymVault.LoadOrCreate(path, pools);
        PseudonymVault stale = PseudonymVault.LoadOrCreate(path, pools);
        first.Remint("s-1", name => name != "Noa" && name != "Noa Bravo");
        first.SaveTo(path);
        string committed = File.ReadAllText(path);

        stale.Remint("s-1", name => name != "Sol" && name != "Sol Bravo");
        Assert.ThrowsAny<InvalidOperationException>(() => stale.SaveTo(path));
        Assert.Equal(committed, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{ "version": "2" }""")]
    [InlineData("""{ "version": "2", "subjects": null }""")]
    [InlineData("""{ "version": "2", "subjects": { "s-1": null } }""")]
    [InlineData("""{ "version": "2", "subjects": { "s-1": { "pseudonym": "" } } }""")]
    [InlineData("""{ "version": "2", "subjects": { "s-1": { "pseudonym": "SIL-1", "retired": null } } }""")]
    [InlineData("""{ "version": "2", "subjects": { "s-1": { "pseudonym": "SIL-1" }, "s-2": { "pseudonym": "SIL-1" } } }""")]
    [InlineData("""{ "version": "2", "subjects": { "s-1": { "pseudonym": "SIL-1", "surrogate": "Ale Bravo" }, "s-2": { "pseudonym": "SIL-2", "surrogate": " ale bravo " } } }""")]
    [InlineData("""{ "version": "2", "subjects": { "s-1": { "pseudonym": "SIL-1", "retired": ["Ale Bravo"] }, "s-2": { "pseudonym": "SIL-2", "surrogate": "Ale Bravo" } } }""")]
    [InlineData("""{ "version": "2", "subjects": { "s-1": { "pseudonym": "SIL-1" }, "s-1": { "pseudonym": "SIL-2" } } }""")]
    public void An_incomplete_or_ambiguous_vault_is_refused(string json)
    {
        Assert.ThrowsAny<InvalidOperationException>(() => PseudonymVault.FromJson(json));
    }

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
