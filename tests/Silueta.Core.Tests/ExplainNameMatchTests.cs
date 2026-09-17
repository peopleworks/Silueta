using Silueta.Core;
using Silueta.Mcp.Tools;

namespace Silueta.Core.Tests;

/// <summary>
/// <c>explain_name_match</c> answers "would Silueta treat these two spellings as the same name?". It used to
/// answer by re-implementing the matcher inside the tool — keys, floor, threshold, weakest word — which is a
/// second copy of the rule, the shape of defect this project keeps finding. The first change to the real
/// matcher after it was written made the copy wrong: short company names now have to be the same letters,
/// and the tool still said "Inc" and "ink" match. So its verdict comes from the detector itself, and this
/// test holds the two to one answer across kinds.
/// </summary>
public class ExplainNameMatchTests
{
    [Theory]
    [InlineData("Ana", "Anna", "PatientName")]
    [InlineData("Reyes", "Rays", "PatientName")]
    [InlineData("Sofía Reyes", "Sophia Reyes", "StaffName")]
    [InlineData("Inc", "ink", "Organization")]
    [InlineData("Zoho", "ZOHO", "Organization")]
    [InlineData("Zoho", "so", "Organization")]
    [InlineData("Zoho", "so", "OtherName")]
    [InlineData("Lyft", "lift", "Product")]
    [InlineData("Acme Corporation", "Acme Corp", "Organization")]
    public void The_tool_and_the_detector_give_one_answer(string rosterValue, string heard, string kind)
    {
        Assert.True(IdentifierKindExtensions.TryParseName(kind, out IdentifierKind parsed));

        var context = new DeidentificationContext("explain").AddValue(rosterValue, parsed, "subject-1");
        List<Token> words = Tokenizer.Tokenize(heard);
        bool detectorSays = new KnownValueDetector()
            .Detect(heard, context)
            .Any(d => d.Start == words[0].Start && d.End == words[^1].End);

        Assert.Equal(detectorSays, MatchingTools.ExplainNameMatch(rosterValue, heard, kind).WouldMatch);
    }

    [Fact]
    public void A_short_company_name_and_the_word_it_sounds_like_are_explained_as_a_miss()
    {
        MatchExplanation explanation = MatchingTools.ExplainNameMatch("Inc", "ink", "Organization");

        Assert.False(explanation.WouldMatch);
        Assert.Contains("same letters", explanation.Words[0].Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_same_pair_for_a_person_is_explained_as_a_match()
    {
        Assert.True(MatchingTools.ExplainNameMatch("Inc", "ink", "OtherName").WouldMatch);
    }

    [Fact]
    public void A_kind_the_tool_cannot_read_is_refused()
    {
        Assert.ThrowsAny<Exception>(() => MatchingTools.ExplainNameMatch("Ana", "Anna", "Organisation"));
    }
}
