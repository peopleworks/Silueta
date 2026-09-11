using Silueta.Core;

namespace Silueta.Core.Tests;

public class PatternDetectorTests
{
    private static readonly DeidentificationContext Empty = new("test");

    [Theory]
    [InlineData("Call her at 602-555-0147 tomorrow.", IdentifierKind.Phone)]
    [InlineData("She wrote to jamileth.v@example.com last night.", IdentifierKind.Email)]
    [InlineData("The appointment is 3/14/2026.", IdentifierKind.Date)]
    [InlineData("Follow-up on September 11, 2026 at the clinic.", IdentifierKind.Date)]
    [InlineData("La cita es el 11 de septiembre de 2026.", IdentifierKind.Date)]
    [InlineData("The patient is 94 years old.", IdentifierKind.AgeOver89)]
    [InlineData("MRN 88213 was updated.", IdentifierKind.RecordNumber)]
    public void Finds_identifiers_that_have_a_shape(string text, IdentifierKind expected)
    {
        List<Detection> found = PatternDetector.FromEmbeddedPack().Detect(text, Empty).ToList();

        Assert.Contains(found, d => d.Kind == expected);
    }

    [Fact]
    public void Leaves_clinical_numbers_alone()
    {
        const string text = "Blood pressure 138 over 82, pain 4 out of 10, pulse was 72.";

        List<Detection> found = PatternDetector.FromEmbeddedPack().Detect(text, Empty).ToList();

        Assert.Empty(found);
    }
}
