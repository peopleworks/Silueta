using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Punto 2, slice D2 — a birth year that puts someone over 89 is not a year Safe Harbor lets through.
/// <para>
/// 45 CFR § 164.514(b)(2)(i)(C) allows the year of a date, and then takes it back for one case: "all ages over
/// 89 and all elements of dates (including year) indicative of such age" may be kept only "aggregated into a
/// single category of age 90 or older". A date of birth is exactly such an element. Until now "born in 1930"
/// came back with 1930 in it, and a manifest said safe-harbor over it.
/// </para>
/// <para>
/// Whether a year implies 90 depends on when you ask, so the run says when: the record's own date when the
/// caller gives one, otherwise the day of the run, and either way it is written into the manifest. The test is
/// deliberately blunt — the reference year minus the birth year, 90 or more — because a birthday nobody stated
/// can fall either side of the run, and the side that removes is the one to fall on.
/// </para>
/// </summary>
public class BirthYearTests
{
    private static RedactionResult Run(string text, DateOnly? recordedOn = null)
    {
        var context = new DeidentificationContext("r-1") { RecordedOn = recordedOn };
        return SiluetaEngine.FromLineage(SiluetaLineage.Default, new PseudonymVault()).Redact(text, context);
    }

    private static readonly DateOnly Today = new(2026, 9, 22);

    [Theory]
    [InlineData("She was born in 1930 and still walks to the store.", "She was born in 90 or older and still walks to the store.")]
    [InlineData("Date of birth: 3/14/1930.", "Date of birth: 90 or older.")]
    [InlineData("DOB 3/14/1930", "DOB 90 or older")]
    [InlineData("Nació el 14 de marzo de 1935.", "Nació el 90 or older.")]
    [InlineData("Her birthday is March 3, 1929.", "Her birthday is 90 or older.")]
    public void A_birth_date_of_somebody_over_89_loses_its_year_too(string text, string expected)
    {
        Assert.Equal(expected, Run(text, Today).Text);
    }

    [Theory]
    [InlineData(1936, "90 or older")] // 2026 - 1936 = 90: she may already be 90, so it goes
    [InlineData(1937, "1937")]        // 89 at most this year, so the year is allowed
    public void The_line_falls_where_the_age_could_be_90(int year, string expected)
    {
        Assert.Equal($"Born in {expected}.", Run($"Born in {year}.", Today).Text);
    }

    [Fact]
    public void A_year_that_is_not_a_birth_year_is_left_alone()
    {
        // Safe Harbor allows a year. What it does not allow is a year that says how old somebody is.
        Assert.Equal("She moved here in 1930 and retired in 1992.", Run("She moved here in 1930 and retired in 1992.", Today).Text);
    }

    [Fact]
    public void The_record_says_when_it_was_recorded_and_that_is_what_decides()
    {
        // The same transcript, read in 2000, is about a woman of 70.
        Assert.Equal("Born in 1930.", Run("Born in 1930.", new DateOnly(2000, 6, 1)).Text);
        Assert.Equal("Born in 90 or older.", Run("Born in 1930.", Today).Text);
    }

    [Fact]
    public void The_manifest_says_which_date_decided_and_which_rule_ran()
    {
        RedactionResult dated = Run("Born in 1930.", Today);
        Assert.Equal("2026-09-22 (record date)", dated.Manifest.AgeReference);
        Assert.StartsWith("birth-year/1", dated.Manifest.BirthYearRule, StringComparison.Ordinal);

        // Without a record date the run's own day decides, and the manifest says so rather than implying the
        // transcript carried one.
        Assert.EndsWith("(run date)", Run("Nothing here.").Manifest.AgeReference, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_from_before_this_rule_says_no_date_decided()
    {
        Assert.Equal("none", new RedactionManifest().AgeReference);
        Assert.Equal("off", new RedactionManifest().BirthYearRule);
    }

    [Fact]
    public void Reading_the_output_back_finds_no_year_to_remove()
    {
        RedactionResult once = Run("She was born in 1930.", Today);
        RedactionResult twice = Run(once.Text, Today);

        Assert.Equal(once.Text, twice.Text);
        Assert.Equal(0, once.Manifest.ResidualSpans);
    }

    [Fact]
    public void An_age_said_as_a_number_is_still_found_the_way_it_always_was()
    {
        Assert.Equal("The patient is 90 or older.", Run("The patient is 94 years old.", Today).Text);
    }
}
