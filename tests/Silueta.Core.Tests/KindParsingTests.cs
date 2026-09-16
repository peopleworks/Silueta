using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// A kind written by a person, in a roster, a lineage or a pattern pack.
/// <para>
/// The roster used to read an unknown kind as <c>OtherName</c> without a word. Before companies existed
/// that cost little. After, it is the defect the company kinds were added to remove, reached by a typo:
/// "Organisation" — the spelling this project's own documents use — sent a company to the pool of
/// people's names. And the parser underneath was <c>Enum.TryParse</c>, which is not a name parser at all:
/// it reads "3" as the third kind and "PatientName, Phone" as both at once.
/// </para>
/// </summary>
public class KindParsingTests
{
    [Theory]
    [InlineData("Organization", IdentifierKind.Organization)]
    [InlineData("organization", IdentifierKind.Organization)]
    [InlineData("  ClientName ", IdentifierKind.ClientName)]
    public void A_kind_is_read_by_its_name_regardless_of_case(string text, IdentifierKind expected)
    {
        Assert.True(IdentifierKindExtensions.TryParseName(text, out IdentifierKind kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("3")]                    // Enum.TryParse: StaffName
    [InlineData("99")]                   // Enum.TryParse: a value no kind has
    [InlineData("PatientName, Phone")]   // Enum.TryParse: both, OR'd together
    [InlineData("Organisation")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_that_is_not_a_kind_s_name_is_not_a_kind(string? text)
    {
        Assert.False(IdentifierKindExtensions.TryParseName(text, out _));
    }

    [Theory]
    [InlineData("Organisation", "Organization")]
    [InlineData("PatientNmae", "PatientName")]
    [InlineData("Eleanor Vasquez", null)]
    public void A_near_miss_gets_a_suggestion_and_a_name_gets_nothing(string text, string? closest)
    {
        // The suggestion is the only thing an error message may say about what was written, and it says it
        // by naming one of the kinds, never by repeating the input. A roster with its columns swapped puts
        // a person's name in the kind field; that must not come back in an error, even as a near miss.
        Assert.Equal(closest, IdentifierKindExtensions.ClosestName(text));
    }

    [Fact]
    public void A_lineage_label_keyed_by_a_number_is_skipped_rather_than_read_as_a_kind()
    {
        SiluetaLineage lineage = SiluetaLineage.FromJson("""
            { "lineage": "t", "version": "1",
              "pools": { "given": ["Ale"], "family": ["Bravo"] },
              "labels": { "3": "[X]" } }
            """);

        Assert.Contains("3", lineage.Skipped);
        Assert.Equal("[STAFF]", SiluetaLineage.Default.LabelFor(IdentifierKind.StaffName));
        Assert.NotEqual("[X]", lineage.LabelFor(IdentifierKind.StaffName));
    }

    [Fact]
    public void A_pattern_rule_whose_kind_is_a_number_is_skipped_rather_than_read_as_a_kind()
    {
        var detector = new PatternDetector([
            new PatternRule { Id = "numbered", Kind = "3", Regex = @"\d{4}" },
        ]);

        Assert.Equal(0, detector.RulesLoaded);
        Assert.Contains("numbered", detector.RulesSkipped);
    }
}
