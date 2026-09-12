using Silueta.Core;

namespace Silueta.Core.Tests;

public class KnownValueDetectorTests
{
    private static DeidentificationContext Roster() => new DeidentificationContext("test")
        .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)
        .AddPerson("family-1", "Yamilet Vasquez", IdentifierKind.FamilyName)
        .AddPerson("staff-1", "Sofía Reyes", IdentifierKind.StaffName);

    [Fact]
    public void Finds_a_name_the_recogniser_misspelled()
    {
        const string text = "Ellenor Vasques was resting when I arrived.";

        List<Detection> found = new KnownValueDetector().Detect(text, Roster()).ToList();

        Assert.Contains(found, d => d.Kind == IdentifierKind.PatientName && d.TextIn(text) == "Ellenor Vasques");
    }

    [Fact]
    public void Finds_a_first_name_on_its_own()
    {
        const string text = "Sophia said she would call the doctor.";

        List<Detection> found = new KnownValueDetector().Detect(text, Roster()).ToList();

        Detection match = Assert.Single(found, d => d.TextIn(text) == "Sophia");
        Assert.Equal(IdentifierKind.StaffName, match.Kind);
        Assert.Equal("staff-1", match.SubjectId);
    }

    [Fact]
    public void Leaves_clinical_words_alone()
    {
        const string text = "Early Parkinson, takes warfarin with breakfast, pain four out of ten.";

        List<Detection> found = new KnownValueDetector().Detect(text, Roster()).ToList();

        Assert.Empty(found);
    }

    [Fact]
    public void Short_words_need_an_exact_match()
    {
        // "Ana" is a name; "una" is a Spanish article one edit away. Below the fuzzy floor, only the
        // name itself counts, or the redactor starts eating the language.
        var roster = new DeidentificationContext("test").AddPerson("p", "Ana", IdentifierKind.PatientName);

        List<Detection> found = new KnownValueDetector().Detect("Fue una visita tranquila.", roster).ToList();

        Assert.Empty(found);
    }
}
