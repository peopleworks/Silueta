using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Every case here was taken from real speech-recognition output, not invented: these are the shapes
/// a name arrives in when nobody typed it.
/// </summary>
public class PhoneticKeyTests
{
    [Theory]
    [InlineData("Navi", "Na'vi")]      // the apostrophe a recogniser invents
    [InlineData("Navi", "Navy")]       // and the word it knows instead
    [InlineData("Sophia", "Sofía")]    // ph/f and the accent
    [InlineData("Yamilet", "Jamileth")] // y/j, and a silent h on the end
    [InlineData("Vasquez", "Vasques")] // z/s
    [InlineData("Herrera", "Errera")]  // silent h, doubled r
    public void Hears_the_same_name_written_two_ways(string a, string b) =>
        Assert.Equal(PhoneticKey.Compute(a), PhoneticKey.Compute(b));

    [Fact]
    public void Close_enough_survives_one_missing_letter()
    {
        // Asked of the tolerance rather than of a number repeated here: what counts as close enough is one
        // rule in one place, and a test carrying its own copy of the figure is the second copy.
        Assert.True(
            MatchTolerance.Default.Accepts(PhoneticKey.Compute("Ellenor"), PhoneticKey.Compute("Eleanor"), out int edits, out int budget),
            $"Ellenor/Eleanor are {edits} edit(s) apart on a budget of {budget}");
    }

    [Fact]
    public void Different_names_stay_different()
    {
        Assert.False(
            MatchTolerance.Default.Accepts(PhoneticKey.Compute("Parkinson"), PhoneticKey.Compute("Patterson"), out int edits, out int budget),
            $"Parkinson/Patterson are {edits} edit(s) apart on a budget of {budget}");
    }

    [Fact]
    public void Words_with_no_letters_have_no_key() => Assert.Equal(string.Empty, PhoneticKey.Compute("  "));
}
