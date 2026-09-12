using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The lineage: what an organisation brings of its own. Until it existed the word lists were two arrays
/// in the source and the labels were a <c>switch</c> in English, so a Spanish-speaking agency got
/// <c>[PHONE]</c> in the middle of a Spanish sentence and could do nothing about it short of forking.
/// <para>
/// Two things are being tested here and they pull in opposite directions. One is that a file from
/// outside decides behaviour, which is the point. The other is that a file from outside decides
/// behaviour, which is how the vault format was nearly turned into a way to destroy vaults — so every
/// rule a pool has to obey is checked at load, in the loader, not discovered later in a corpus.
/// </para>
/// </summary>
public class LineageTests
{
    private const string Minimal = """
        {
          "lineage": "test", "version": "1", "language": "es-MX",
          "pools": { "given": ["Ale", "Noa"], "family": ["Bravo", "Toledo"] }
        }
        """;

    private static string With(string body) => $$"""
        {
          "lineage": "test", "version": "1",
          "pools": { "given": ["Ale", "Noa"], "family": ["Bravo", "Toledo"] },
          {{body}}
        }
        """;

    private static DeidentificationContext Roster(string record = "lin-1") =>
        new DeidentificationContext(record)
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "pools": { "given": ["Ale"], "family": ["Bravo"] } }""")]
    [InlineData("""{ "lineage": "test" }""")]
    [InlineData("""{ "version": "1" }""")]
    [InlineData("""[ { "value": "Eleanor Vasquez" } ]""")]
    public void A_file_that_is_not_a_lineage_is_refused_rather_than_read_as_an_empty_one(string json)
    {
        // Exactly the mistake the vault format made: a required field with a harmless default turns
        // every JSON object in the world into a valid, empty file of this type.
        Assert.Throws<InvalidOperationException>(() => SiluetaLineage.FromJson(json));
    }

    [Fact]
    public void A_lineage_with_no_words_to_draw_from_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => SiluetaLineage.FromJson(
            """{ "lineage": "t", "version": "1", "pools": { "given": [], "family": ["Bravo"] } }"""));
    }

    [Theory]
    [InlineData("""["Ale", "Álex", "Alex"]""", "twice")]          // folded duplicate
    [InlineData("""["Ale", "Noa2"]""", "digit")]                   // a number reads as a record number
    [InlineData("""["Vasquez", "Vasques"]""", "sound alike")]      // the matcher cannot tell them apart
    public void A_pool_the_vault_could_not_use_safely_is_refused_at_load(string given, string because)
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            SiluetaLineage.FromJson(
                $$"""{ "lineage": "t", "version": "1", "pools": { "given": {{given}}, "family": ["Bravo"] } }"""));

        Assert.Contains(because, thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_fingerprint_follows_the_content_and_not_the_version_or_the_order()
    {
        SiluetaLineage baseline = SiluetaLineage.FromJson(Minimal);

        // Same words in a different order is the same lineage: the vault shuffles the pool before
        // minting, so line order changes nothing about what can come out.
        Assert.Equal(baseline.Fingerprint, SiluetaLineage.FromJson("""
            {
              "lineage": "test", "version": "1", "language": "es-MX",
              "pools": { "given": ["Noa", "Ale"], "family": ["Toledo", "Bravo"] }
            }
            """).Fingerprint);

        // One word changed, version untouched: an editor who forgets to bump the version still produces
        // a corpus that can be told apart from the one before it.
        Assert.NotEqual(baseline.Fingerprint, SiluetaLineage.FromJson("""
            {
              "lineage": "test", "version": "1", "language": "es-MX",
              "pools": { "given": ["Ale", "Sol"], "family": ["Bravo", "Toledo"] }
            }
            """).Fingerprint);
    }

    [Fact]
    public void The_label_that_lands_in_the_text_is_the_lineage_s_own()
    {
        SiluetaLineage spanish = SiluetaLineage.FromJson(With("""
            "labels": { "Phone": "[TELÉFONO]", "Email": "[CORREO]" }
            """));

        RedactionResult result = SiluetaEngine.FromLineage(spanish)
            .Redact("Llamó al 602-555-0147 y escribió a ana@example.com.", Roster());

        Assert.Contains("[TELÉFONO]", result.Text, StringComparison.Ordinal);
        Assert.Contains("[CORREO]", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[PHONE]", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void So_is_the_wider_value_that_replaces_an_age()
    {
        SiluetaLineage spanish = SiluetaLineage.FromJson(With("""
            "generalizations": { "AgeOver89": "90 o más" }
            """));

        RedactionResult result = SiluetaEngine.FromLineage(spanish)
            .Redact("La paciente tiene 94 years old.", Roster());

        Assert.Contains("90 o más", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lineage_with_rules_of_its_own_replaces_the_pack_rather_than_adding_to_it()
    {
        // Two sources for one rule is two rules that will disagree. A lineage that brings patterns is
        // saying "these are the shapes I care about", and the phone rule of the built-in pack is not
        // one of them unless it says so.
        SiluetaLineage own = SiluetaLineage.FromJson(With("""
            "patterns": [ { "id": "badge", "kind": "RecordNumber", "regex": "BADGE-\\d+", "confidence": 0.95 } ]
            """));

        RedactionResult result = SiluetaEngine.FromLineage(own)
            .Redact("BADGE-4417 called 602-555-0147.", Roster());

        Assert.DoesNotContain("BADGE-4417", result.Text, StringComparison.Ordinal);
        Assert.Contains("602-555-0147", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_naming_a_kind_this_build_does_not_know_is_recorded_rather_than_fatal()
    {
        SiluetaLineage lineage = SiluetaLineage.FromJson(With("""
            "labels": { "Phone": "[TEL]", "Aseguradora": "[X]" }
            """));

        Assert.Contains("Aseguradora", lineage.Skipped);
        Assert.Equal("[TEL]", lineage.LabelFor(IdentifierKind.Phone));
    }

    [Fact]
    public void A_pool_entry_of_two_words_is_one_name_and_not_two()
    {
        // The pools used to hold exactly one word per entry, so "the head of a surrogate" and
        // "everything before the first space" were the same string and both the vault and the fitter
        // took the second one. The first lineage written in Spanish breaks that: María José is one
        // name, and a one-word mention replaced by "María" is a name nobody minted, nobody checked
        // against the roster, and the vault cannot look up.
        SiluetaLineage lineage = SiluetaLineage.FromJson("""
            {
              "lineage": "es", "version": "1",
              "pools": { "given": ["María José"], "family": ["De la Cruz"] }
            }
            """);

        var engine = SiluetaEngine.FromLineage(lineage);
        RedactionResult result = engine.Redact("Eleanor rested well.", Roster());

        Assert.Equal("María José De la Cruz", engine.Vault.SurrogateFor("patient-1"));
        Assert.Contains("María José rested well.", result.Text, StringComparison.Ordinal);
        Assert.True(engine.Vault.TryFindSubjectBySurrogate("María José De la Cruz", out _));
    }

    [Fact]
    public void The_built_in_lineage_is_the_library_this_project_always_had()
    {
        // The pools moved out of the source and into an embedded JSON file. That is the whole point —
        // "the dictionaries do not live in the code" has to be literally true — but it is also exactly
        // how a refactor quietly changes behaviour.
        SiluetaLineage builtin = SiluetaLineage.Default;

        Assert.Contains("Urena", builtin.Pools.Family);
        Assert.Contains("Ale", builtin.Pools.Given);
        Assert.Equal(21, builtin.Pools.Given.Count);
        Assert.Equal(22, builtin.Pools.Family.Count);
        Assert.Equal("[PHONE]", builtin.LabelFor(IdentifierKind.Phone));
        Assert.Equal("90 or older", builtin.GeneralizationFor(IdentifierKind.AgeOver89));
        Assert.Empty(builtin.Skipped);
    }

    [Fact]
    public void The_manifest_says_which_lineage_produced_the_corpus()
    {
        SiluetaLineage lineage = SiluetaLineage.FromJson(Minimal);

        RedactionManifest manifest = SiluetaEngine.FromLineage(lineage)
            .Redact("Eleanor Vasquez rested.", Roster()).Manifest;

        Assert.Equal("test", manifest.Lineage);
        Assert.Equal("1", manifest.LineageVersion);
        Assert.Equal("es-MX", manifest.LineageLanguage);
        Assert.Equal(lineage.Fingerprint, manifest.LineageFingerprint);
        Assert.NotEqual(
            manifest.LineageFingerprint,
            SiluetaEngine.CreateDefault().Redact("Eleanor Vasquez rested.", Roster()).Manifest.LineageFingerprint);
    }

    [Fact]
    public void A_vault_that_already_minted_names_keeps_its_own_pools()
    {
        // Swapping the lineage under a vault does not rename anybody: the corpus already says those
        // names. The vault is what minted them, so the vault is what knows how they come apart.
        var vault = new PseudonymVault(SiluetaLineage.Default.Pools);
        string first = vault.SurrogateFor("patient-1");

        var engine = SiluetaEngine.FromLineage(SiluetaLineage.FromJson(Minimal), vault);

        Assert.Equal(first, engine.Vault.SurrogateFor("patient-1"));
        Assert.Same(SiluetaLineage.Default.Pools, engine.Vault.Pools);
    }
}
