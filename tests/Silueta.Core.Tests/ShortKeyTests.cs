using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// A phonetic key shorter than four characters is matched only exactly, and the detector's own comment
/// explains why: short keys are where fuzzy matching starts eating real words. That was meant as the safe
/// setting, and for a company or a product it is not. Measured before this file existed: "Inc" on the
/// roster has the key "ink" and redacted "The ink cartridge is empty"; "Zoho" has the key "so" and redacted
/// every "so" in a sales call; "HP" has the key "p" and matched a bare "P". Exact equality of keys is not
/// exact equality of words — the key exists precisely to make different spellings equal.
/// <para>
/// The fix is by kind and by length, and deliberately no wider. For a <b>person</b> nothing changes:
/// tolerating what a recogniser does to a name is the whole thesis, and "Ana" heard as "Anna" has to stay
/// caught. For an organisation or a product with a short key, the spelling has to match as well — case
/// and accents aside. Whether phonetic tolerance helps or hurts brand names at <em>any</em> length is a
/// real question, and it is answered by measuring against a corpus, not by this change; the last two
/// tests pin where it is still open so the day it is decided they fail.
/// </para>
/// </summary>
public class ShortKeyTests
{
    private static RedactionResult Redact(string text, string value, IdentifierKind kind)
    {
        var roster = new DeidentificationContext("short-key").AddPerson("subject-1", value, kind);
        return SiluetaEngine.CreateDefault().Redact(text, roster);
    }

    [Theory]
    [InlineData("So we called them, and so on.", "Zoho")]
    [InlineData("The ink cartridge is empty.", "Inc")]
    [InlineData("We chose option P for the rollout.", "HP")]
    public void A_short_company_name_does_not_redact_the_ordinary_words_it_sounds_like(string text, string company)
    {
        RedactionResult result = Redact(text, company, IdentifierKind.Organization);

        Assert.Equal(text, result.Text);
        Assert.Empty(result.Applied);
    }

    [Theory]
    [InlineData("We moved the invoices to ZOHO last year.")]
    [InlineData("We moved the invoices to Zohó last year.")]
    public void A_short_company_name_is_still_found_whatever_its_case_or_accents(string text)
    {
        RedactionResult result = Redact(text, "Zoho", IdentifierKind.Organization);

        Assert.Equal("We moved the invoices to [ORGANIZATION] last year.", result.Text);
    }

    [Fact]
    public void A_short_product_name_follows_the_same_rule()
    {
        RedactionResult result = Redact("The ink cartridge is empty.", "Inc", IdentifierKind.Product);

        Assert.Empty(result.Applied);
    }

    [Fact]
    public void A_short_persons_name_is_still_matched_by_how_it_sounds()
    {
        // The thesis. "Ana" as the agency writes it and "Anna" as the recogniser spelled it are one person,
        // and a leak of a patient's name is worse than any number of redacted function words.
        RedactionResult result = Redact("Anna came by this morning.", "Ana", IdentifierKind.PatientName);

        Assert.DoesNotContain("Anna", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_longer_brand_that_sounds_like_a_word_is_still_matched_which_is_not_decided_here()
    {
        // "Lyft" has a four-character key, "lift", and matches the English word. Past the short-key floor,
        // whether a brand should be compared by sound at all is the open question — this pins that it is
        // still open. When it is decided, this test changes on purpose.
        RedactionResult result = Redact("Can you give me a lift?", "Lyft", IdentifierKind.Organization);

        Assert.NotEmpty(result.Applied);
    }

    [Fact]
    public void A_brand_one_letter_from_a_spanish_word_is_still_matched_which_is_not_decided_here()
    {
        // "Nvidia" against "envidia" scores 0.857 against a threshold of 0.84. Same open question.
        RedactionResult result = Redact("Le tiene envidia a su hermana.", "Nvidia", IdentifierKind.Organization);

        Assert.NotEmpty(result.Applied);
    }
}
