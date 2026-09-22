using System.Text.Json.Nodes;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Punto 2, second slice — a postal code keeps what Safe Harbor lets it keep, and the census decides what that is.
/// <para>
/// Pedro, 22 September 2026: state, city and ZIP are statistics; an exact address is a leak. Safe Harbor agrees
/// about the ZIP up to a point: 45 CFR § 164.514(b)(2)(i)(B) keeps its first three digits where the area they
/// name holds more than 20,000 people "according to the current publicly available data from the Bureau of the
/// Census", and turns the rest into 000. Until now the whole code went, because the table that decides was not
/// in the package.
/// </para>
/// <para>
/// The table is the 2020 census, not the seventeen prefixes HHS printed in 2012. Those came from the 2000
/// count, and the guidance says in the same paragraph not to rely on them once newer data is published. Against
/// 2020 they keep six prefixes that must be zeroed and zero five that may be kept. And the table is an allow-list:
/// a prefix the census has no area for — military mail, unassigned ranges — has nobody counted in it, so it
/// becomes 000 rather than passing because it was not on a list of small ones.
/// </para>
/// </summary>
public class PostalCodeTests
{
    private static RedactionResult Run(string text, SiluetaPolicy? policy = null, SiluetaLineage? lineage = null) =>
        SiluetaEngine.FromLineage(lineage ?? SiluetaLineage.Default, new PseudonymVault())
            .Redact(text, new DeidentificationContext("r-1"), policy ?? SiluetaPolicy.SafeHarbor);

    private static SiluetaLineage With(string key, string json)
    {
        JsonNode lineage = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Repo.Root, "src", "Silueta.Core", "Lineage", "Lineages", "lineage.core.json")))!;
        lineage["lineage"] = "clinic-test";
        lineage[key] = JsonNode.Parse(json);
        return SiluetaLineage.FromJson(lineage.ToJsonString());
    }

    [Theory]
    [InlineData("85004", "850XX")]      // Phoenix: a million and a half people behind 850
    [InlineData("85004-1234", "850XX")] // the four digits after the dash go with the last two
    [InlineData("00601", "006XX")]      // Puerto Rico is in the count
    [InlineData("05901", "000XX")]      // 059 holds 3,352 people: zeroed in 2000, zeroed in 2020
    [InlineData("09012", "000XX")]      // 090 is military mail: no area, nobody counted, nothing kept
    public void A_postal_code_keeps_three_digits_only_where_the_census_counts_more_than_20000_people(
        string written, string expected)
    {
        Assert.Equal(expected, CensusZipTable.Generalize(written));
    }

    [Theory]
    [InlineData("850")]
    [InlineData("8500")]
    [InlineData("850041")]
    public void Something_that_is_not_a_five_or_nine_digit_code_is_not_generalised(string written)
    {
        // The engine labels it instead. Three digits of a number that is not a ZIP are not an area.
        Assert.Null(CensusZipTable.Generalize(written));
    }

    [Theory]
    [InlineData("202")]
    [InlineData("204")]
    [InlineData("205")]
    [InlineData("369")]
    [InlineData("753")]
    [InlineData("772")]
    public void Six_prefixes_the_2012_guidance_list_would_have_kept_are_zeroed(string prefix)
    {
        Assert.False(CensusZipTable.MayKeep(prefix));
        Assert.Equal("000XX", CensusZipTable.Generalize(prefix + "01"));
    }

    [Theory]
    [InlineData("063")]
    [InlineData("790")]
    [InlineData("830")]
    [InlineData("831")]
    [InlineData("890")] // Las Vegas: 708,276 people in 2020
    public void Five_prefixes_the_2012_list_zeroes_have_grown_past_the_threshold(string prefix)
    {
        // Pinned so that nobody "fixes" the table back to the list in the guidance.
        Assert.True(CensusZipTable.MayKeep(prefix));
    }

    [Fact]
    public void The_table_is_the_2020_count_it_says_it_is()
    {
        Assert.Equal("zip3/census-2020", CensusZipTable.Table);
        Assert.Equal(20_000, CensusZipTable.Threshold);
        Assert.Equal("2026-09-22", CensusZipTable.Retrieved);
        Assert.Equal("950727a74b2f9912fb0a5eafe0c3e6f6e69ef35af858ed9d7f1396b854e806ea", CensusZipTable.SourceSha256);

        // The populations add up to the total the Census Bureau served, as the file records it, so one number
        // edited by hand shows.
        JsonNode table = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Repo.Root, "src", "Silueta.Core", "Transform", "Tables", "zip3.census-2020.json")))!;
        Assert.Equal(334_726_586L, (long)table["source"]!["population"]!);
        Assert.Equal(894, CensusZipTable.Prefixes.Count);
        Assert.Equal((long)table["source"]!["population"]!, CensusZipTable.Prefixes.Sum(prefix => (long)CensusZipTable.PopulationOf(prefix)!.Value));
        Assert.Equal(
            ["036", "059", "102", "202", "203", "204", "205", "369", "556", "692", "753", "772", "821", "823", "878", "879", "884", "893"],
            CensusZipTable.Prefixes.Where(prefix => !CensusZipTable.MayKeep(prefix)));
        Assert.Null(CensusZipTable.PopulationOf("090"));
        Assert.Equal("7b115ccec7ba11ab", CensusZipTable.Fingerprint);
    }

    [Theory]
    [InlineData("My zip code is 85004, near the clinic.", "My zip code is 850XX, near the clinic.")]
    [InlineData("ZIP: 85004-1234", "ZIP: 850XX")]
    [InlineData("Su código postal es 85004.", "Su código postal es 850XX.")]
    [InlineData("codigo postal 05901", "codigo postal 000XX")]
    public void A_postal_code_after_zip_or_codigo_postal_is_found_and_the_words_around_it_stay(string text, string expected)
    {
        Assert.Equal(expected, Run(text).Text);
    }

    [Theory]
    [InlineData("Revisé su expediente, número 77314, y el antecedente.")]
    [InlineData("I checked his record number 77314 and the notes.")]
    [InlineData("She lives at 85004 now.")]
    public void Five_digits_with_nothing_saying_zip_before_them_are_not_taken_for_a_postal_code(string text)
    {
        // A bare five-digit rule would read every record number, amount and count as a postal code. What
        // this costs is written in the caveat: a code nobody introduced as one is not found.
        Assert.DoesNotContain("PostalCode", Run(text).Manifest.ByKind.Keys);
    }

    [Theory]
    [InlineData("85004", "Moved to 850XX last spring.")]
    [InlineData("SW1A 1AA", "Moved to [ZIP] last spring.")] // not a US code: labelled, never guessed at
    public void A_postal_code_on_the_roster_is_widened_the_same_way(string zip, string expected)
    {
        // The path callers had before the rule existed: a roster entry of kind PostalCode. It came back as
        // [ZIP]; now it keeps what the census allows, like one the rule finds.
        var context = new DeidentificationContext("r-1").AddValue(zip, IdentifierKind.PostalCode, "place-1");
        RedactionResult result = SiluetaEngine.FromLineage(SiluetaLineage.Default, new PseudonymVault())
            .Redact($"Moved to {zip} last spring.", context);

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void The_manifest_names_the_table_that_decided()
    {
        Assert.Equal($"zip3/census-2020 {CensusZipTable.Fingerprint}", Run("zip 85004").Manifest.PostalCodeTable);
    }

    [Fact]
    public void A_manifest_from_before_the_table_says_no_table_decided()
    {
        // Before this, the whole code went. A manifest that omits the field must not read as one that kept 850.
        Assert.Equal("none", new RedactionManifest().PostalCodeTable);
    }

    [Fact]
    public void A_policy_that_keeps_postal_codes_keeps_all_five_digits_and_says_so()
    {
        SiluetaLineage lineage = With("policies", """
            { "statistics": { "version": "1", "actions": { "PostalCode": "Keep" } } }
            """);
        RedactionResult result = Run("zip 85004", lineage.Policy("statistics"), lineage);

        Assert.Equal("zip 85004", result.Text);
        Assert.Contains("PostalCode: Keep (Safe Harbor: Generalize)", result.Manifest.DeparturesFromSafeHarbor);
        Assert.Equal("unused (PostalCode: Keep)", result.Manifest.PostalCodeTable);
    }

    [Fact]
    public void A_lineage_cannot_give_postal_codes_a_generalisation_of_its_own()
    {
        // What a ZIP keeps is Safe Harbor's rule and the census's number. An organisation that wants the whole
        // code gone says so in a policy — "PostalCode": "Label" — where the manifest lists it as a departure.
        SiluetaLineage lineage = With("generalizations", """{ "AgeOver89": "90 or older", "PostalCode": "[AREA]" }""");

        Assert.Contains("generalizations.PostalCode", lineage.Skipped);
        Assert.Equal("zip 850XX", Run("zip 85004", lineage: lineage).Text);
    }
}
