using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The first baseline: the same roster, matched literally. It is what anyone can build in ten minutes with
/// a general PII tool and the list they already have, and the distance between it and Silueta is the value
/// of the phonetic thesis — so it has to be exactly as dumb as that, no dumber and no smarter.
/// </summary>
public class DenyListDetectorTests
{
    private static IReadOnlyList<Detection> Find(string text, DeidentificationContext roster) =>
        [.. new DenyListDetector().Detect(text, roster)];

    [Fact]
    public void It_finds_a_roster_value_spelled_the_way_the_roster_spells_it()
    {
        var roster = new DeidentificationContext("b").AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);

        Assert.Contains(Find("Eleanor Vasquez rested.", roster), d => d.Start == 0 && d.Length == 15);
    }

    [Fact]
    public void Case_and_accents_are_not_spelling()
    {
        // A literal match that missed "SOFIA" for "Sofía" would make the baseline a straw man, and a thesis
        // that beats a straw man proves nothing.
        var roster = new DeidentificationContext("b").AddPerson("staff-1", "Sofía Reyes", IdentifierKind.StaffName);

        Assert.NotEmpty(Find("SOFIA REYES signed.", roster));
    }

    [Fact]
    public void It_does_not_hear_anything()
    {
        var roster = new DeidentificationContext("b").AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);

        Assert.Empty(Find("Ellenor Vasques rested.", roster));
    }

    [Fact]
    public void It_finds_a_first_name_alone_because_the_roster_registered_it()
    {
        var roster = new DeidentificationContext("b").AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);

        Assert.Contains(Find("Eleanor rested.", roster), d => d.Start == 0 && d.Length == 7);
    }

    [Fact]
    public void It_does_not_match_inside_a_longer_word()
    {
        var roster = new DeidentificationContext("b").AddPerson("patient-1", "Ana Ruiz", IdentifierKind.PatientName);

        Assert.Empty(Find("The banana was ripe.", roster));
    }
}
