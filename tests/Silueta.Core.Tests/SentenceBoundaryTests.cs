using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// A roster value of N words is matched against N adjacent words of the transcript, and the window used
/// to be blind to what sat between them. Measured before this file existed: with "Acme Corporation" on the
/// roster, "We called Acme. Corporation tax is due next month." came back as "We called Ariel Gaitan tax
/// is due next month." — two sentences fused into one, a full stop eaten, and "Corporation" in the second
/// sentence replaced although it identified nobody.
/// <para>
/// The fix has a direction, and it is not symmetric. A window that stops at a real boundary costs, at
/// worst, a name split across one being missed whole — and a person is still caught by the parts of their
/// name, registered separately. A window that stops at a <em>false</em> boundary can cost a leak: a
/// company is registered only whole, so if a line wrap counted as a boundary, a transcript wrapped at a
/// fixed width that split "Acme" from "Corporation" would lose the company name entirely. So a sentence
/// end and a blank line stop the window; a single line break does not.
/// </para>
/// </summary>
public class SentenceBoundaryTests
{
    private static RedactionResult Redact(string text, string value, IdentifierKind kind)
    {
        var roster = new DeidentificationContext("boundary").AddPerson("subject-1", value, kind);
        return SiluetaEngine.CreateDefault().Redact(text, roster);
    }

    [Theory]
    [InlineData("We called Acme. Corporation tax is due next month.")]
    [InlineData("Did you call Acme? Corporation tax is due next month.")]
    [InlineData("We called Acme! Corporation tax is due next month.")]
    [InlineData("We called Acme… Corporation tax is due next month.")]
    [InlineData("We called Acme\n\nCorporation tax is due next month.")]
    public void A_company_is_not_found_across_the_end_of_a_sentence(string text)
    {
        RedactionResult result = Redact(text, "Acme Corporation", IdentifierKind.Organization);

        // Nothing here names the company: "Acme" alone is not on the roster, and "Corporation" in the next
        // sentence is a tax. The text has to come back exactly as it went in.
        Assert.Equal(text, result.Text);
        Assert.Empty(result.Applied);
    }

    [Fact]
    public void A_person_split_by_a_full_stop_is_still_caught_word_by_word_and_the_sentences_stay_two()
    {
        const string text = "The visit was with Eleanor. Vasquez forms arrived later.";

        RedactionResult result = Redact(text, "Eleanor Vasquez", IdentifierKind.PatientName);

        // No leak: a person is registered by the parts of their name as well, so each half is found on
        // its own. And no fusion: two spans, the full stop between them untouched.
        Assert.DoesNotContain("Eleanor", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Vasquez", result.Text, StringComparison.Ordinal);
        Assert.Equal(2, result.Applied.Count);
        Assert.Matches(@"^The visit was with \S+\. \S+ forms arrived later\.$", result.Text);
    }

    [Fact]
    public void A_company_split_by_a_line_wrap_is_still_found()
    {
        // A single line break is how a transcript wrapped at a fixed width looks. Treating it as a boundary
        // would lose the company name completely, since a company is never registered word by word.
        RedactionResult result = Redact(
            "We called Acme\nCorporation about the delay.", "Acme Corporation", IdentifierKind.Organization);

        Assert.DoesNotContain("Acme", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Corporation", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_stop_the_roster_itself_writes_is_not_a_boundary()
    {
        // "St. Mary Hospital" has a full stop between its first two words, and so does the roster entry.
        // A boundary is punctuation the text has and the name does not.
        RedactionResult result = Redact(
            "She was admitted to St. Mary Hospital today.", "St. Mary Hospital", IdentifierKind.Organization);

        Assert.Equal("She was admitted to [ORGANIZATION] today.", result.Text);
    }

    [Fact]
    public void The_full_stop_after_an_initial_is_not_the_end_of_a_sentence()
    {
        RedactionResult result = Redact(
            "Then John F. Kennedy called back.", "John F Kennedy", IdentifierKind.OtherName);

        Assert.DoesNotContain("Kennedy", result.Text, StringComparison.Ordinal);
        Assert.Single(result.Applied);
    }
}
