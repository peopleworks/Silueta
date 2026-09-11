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
        double ratio = Similarity.Ratio(PhoneticKey.Compute("Ellenor"), PhoneticKey.Compute("Eleanor"));
        Assert.True(ratio >= 0.84, $"Ellenor/Eleanor scored {ratio:0.000}");
    }

    [Fact]
    public void Different_names_stay_different()
    {
        double ratio = Similarity.Ratio(PhoneticKey.Compute("Parkinson"), PhoneticKey.Compute("Patterson"));
        Assert.True(ratio < 0.84, $"Parkinson/Patterson scored {ratio:0.000}");
    }

    [Fact]
    public void Words_with_no_letters_have_no_key() => Assert.Equal(string.Empty, PhoneticKey.Compute("  "));
}
