using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Slice E1 — what a recogniser writes when somebody says a date, an e-mail address or an age out loud.
/// <para>
/// The corpus said this first and the README has published it since: the pattern rules miss what a transcript
/// writes for things that were dictated — "September eleventh", "tuan.nguyen at example.com". A rule that only
/// reads <c>9/11</c> and <c>@</c> reads the written form of a spoken medium, which is the one thing this
/// library exists not to do.
/// </para>
/// <para>
/// These three are the low-risk half of that gap, and they are deliberately anchored: a month name, a
/// top-level domain, the words "years old". The dictated digit strings — "five five five, oh one four seven" —
/// are the other half and go in their own slice, because a run of number words is also how a nurse reads a
/// blood pressure out loud, and that is a figure this library keeps.
/// </para>
/// </summary>
public class SpokenTests
{
    private static string Run(string text) =>
        SiluetaEngine.FromLineage(SiluetaLineage.Default, new PseudonymVault())
            .Redact(text, new DeidentificationContext("r-1") { RecordedOn = new DateOnly(2026, 9, 22) }).Text;

    [Theory]
    [InlineData("The appointment is September eleventh.", "The appointment is [DATE].")]
    [InlineData("She fell on the third of March.", "She fell on [DATE].")]
    [InlineData("Discharged October twenty-first, 2025.", "Discharged 2025.")]
    [InlineData("La cita es el once de septiembre.", "La cita es el [DATE].")]
    [InlineData("Ingresó el primero de marzo de 2026.", "Ingresó el 2026.")]
    public void A_date_said_out_loud_is_a_date(string text, string expected)
    {
        // Safe Harbor keeps the year and nothing finer, so a spoken date with a year written beside it keeps
        // that year, and one without loses everything.
        Assert.Equal(expected, Run(text));
    }

    [Theory]
    [InlineData("Write to tuan.nguyen at example.com about it.", "Write to [EMAIL] about it.")]
    [InlineData("Es jamileth arroba ejemplo punto com.", "Es [EMAIL].")]
    [InlineData("It is maria dot lopez at clinic dot org.", "It is [EMAIL].")]
    public void An_e_mail_address_said_out_loud_is_an_e_mail_address(string text, string expected)
    {
        Assert.Equal(expected, Run(text));
    }

    [Theory]
    [InlineData("She is ninety-four years old.", "She is 90 or older.")]
    [InlineData("Tiene noventa y seis años.", "Tiene 90 or older.")]
    [InlineData("Age 96, lives alone.", "90 or older, lives alone.")]
    [InlineData("Edad: 94.", "90 or older.")]
    public void An_age_over_89_counts_however_it_was_written(string text, string expected)
    {
        Assert.Equal(expected, Run(text));
    }

    [Theory]
    [InlineData("Blood pressure 138 over 82, pain 4 out of 10.")]
    [InlineData("Her sugar is 96 this morning.")]
    [InlineData("She takes ninety milligrams twice a day.")]
    [InlineData("Weight ninety-four kilos.")]
    [InlineData("Meet me at the clinic at noon.")]
    [InlineData("He is eighty-eight years old.")]
    public void A_clinical_figure_is_not_an_identifier_and_stays(string text)
    {
        // Pedro's rule for this library: dates, ages, weights and vital signs are the statistics an analysis is
        // made of. An age rule that read "sugar is 96" or "ninety milligrams" would take the analysis with it.
        Assert.Equal(text, Run(text));
    }

    [Fact]
    public void The_months_and_the_days_are_one_list_each()
    {
        // The rules for a date written in digits and a date written in words both need the month names. Two
        // copies drift: a month added to one is a date found in English and missed in Spanish.
        Assert.Contains("month-en", PatternLists.Names);
        Assert.Contains("month-es", PatternLists.Names);
        Assert.Contains("September", PatternLists.Expand("{{month-en}}"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("septiembre", PatternLists.Expand("{{month-es}}"), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(PatternDetector.FromEmbeddedPack().RulesSkipped);
    }
}
