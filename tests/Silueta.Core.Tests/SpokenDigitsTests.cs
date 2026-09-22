using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Slice E2 — a number dictated digit by digit.
/// <para>
/// "Call me at five five five, oh one four seven." A recogniser writes what it heard, and a rule that reads
/// <c>555-0147</c> reads nothing here. This is the higher-risk half of what is said out loud, and the reason is in
/// the same transcript: a nurse reads a blood pressure aloud too. So the rule reads single digits and nothing
/// else — "one thirty eight over eighty two" has tens in it and never forms a run — and it needs either six
/// digits in a row or a word before them that says what they are.
/// </para>
/// <para>
/// What the run is comes from that word when there is one: a phone, a record, an account, a ZIP. Without one, a
/// run as long as a telephone number is a telephone number and any other run is an identifier of no named kind,
/// labelled as such. A run that only counts — "nine eight seven six five four" — is somebody counting, which in
/// home care is a cognitive test, and it stays.
/// </para>
/// </summary>
public class SpokenDigitsTests
{
    private static string Run(string text) =>
        SiluetaEngine.FromLineage(SiluetaLineage.Default, new PseudonymVault())
            .Redact(text, new DeidentificationContext("r-1")).Text;

    [Theory]
    [InlineData("Call me at five five five, oh one four seven.", "Call me at [PHONE].")]
    [InlineData("My number is six oh two, five five five, oh one four seven.", "My number is [PHONE].")]
    [InlineData("Su teléfono es seis cero dos cinco cinco cinco cero uno cuatro siete.", "Su teléfono es [PHONE].")]
    [InlineData("The record number is four four one seven two nine.", "The record number is [RECORD].")]
    [InlineData("Número de expediente: cuatro cuatro uno siete dos nueve.", "Número de expediente: [RECORD].")]
    [InlineData("Member ID nine eight two two one three three four.", "Member ID [ACCOUNT].")]
    [InlineData("It was six oh two five five five oh one four seven, the landline.", "It was [PHONE], the landline.")]
    [InlineData("The code was three one eight two two seven, she said.", "The code was [REMOVED], she said.")]
    public void A_number_dictated_digit_by_digit_is_found_and_named_by_what_introduced_it(string text, string expected)
    {
        Assert.Equal(expected, Run(text));
    }

    [Fact]
    public void A_ZIP_said_out_loud_keeps_what_the_census_allows()
    {
        // The census table reads digits. Said out loud, the digits are words, and the same three stay.
        Assert.Equal("Zip code 850XX.", Run("Zip code eight five zero zero four."));
    }

    [Theory]
    [InlineData("Blood pressure one thirty eight over eighty two.")]
    [InlineData("Pain four out of ten, temperature ninety eight point six.")]
    [InlineData("Count backwards: nine eight seven six five four three two one.")]
    [InlineData("Cuente: uno dos tres cuatro cinco seis siete.")]
    [InlineData("She took two, then three pills.")]
    [InlineData("Room four one two, bed two.")]
    [InlineData("Dos o tres veces al día.")]
    public void A_figure_or_a_count_said_out_loud_is_left_alone(string text)
    {
        Assert.Equal(text, Run(text));
    }

    [Theory]
    [InlineData("six oh two", "602")]
    [InlineData("cinco, cinco - cinco", "555")]
    [InlineData("oh one four seven", "0147")]
    [InlineData("five hundred", null)]
    [InlineData("", null)]
    public void Digits_said_as_words_read_back_as_digits(string words, string? digits)
    {
        Assert.Equal(digits, SpokenDigits.ToDigits(words));
    }

    [Fact]
    public void The_manifest_names_the_rule_and_a_manifest_from_before_says_it_was_off()
    {
        RedactionResult result = SiluetaEngine.FromLineage(SiluetaLineage.Default, new PseudonymVault())
            .Redact("Nothing here.", new DeidentificationContext("r-1"));

        Assert.StartsWith("spoken-digits/1", result.Manifest.SpokenDigitsRule, StringComparison.Ordinal);
        Assert.Equal("off", new RedactionManifest().SpokenDigitsRule);
    }

    [Fact]
    public void The_digit_words_are_data_and_every_digit_has_one()
    {
        // One list, with the value of each word beside it, because the rule has to read a run back as digits —
        // to tell a count from a number and to hand a ZIP to the census — and a word list without values cannot.
        for (int digit = 0; digit <= 9; digit++)
        {
            Assert.NotEmpty(SpokenDigits.WordsFor(digit));
        }

        Assert.Contains("digit-word", PatternLists.Names);
    }
}
