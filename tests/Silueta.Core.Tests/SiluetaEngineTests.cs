using Silueta.Core;

namespace Silueta.Core.Tests;

public class SiluetaEngineTests
{
    private static DeidentificationContext Roster() => new DeidentificationContext("shift-001")
        .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)
        .AddPerson("family-1", "Yamilet Vasquez", IdentifierKind.FamilyName)
        .AddPerson("staff-1", "Sofía Reyes", IdentifierKind.StaffName);

    [Fact]
    public void Removes_the_names_the_recogniser_mangled()
    {
        const string text = "Ellenor Vasques slept well. Jamileth called. Sophia signed the note.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        Assert.DoesNotContain("Ellenor", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jamileth", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sophia", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_the_clinical_content()
    {
        const string text = "Sophia noted blood pressure 138 over 82, pain 4 out of 10, warfarin with breakfast.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        Assert.Contains("138 over 82", result.Text, StringComparison.Ordinal);
        Assert.Contains("pain 4 out of 10", result.Text, StringComparison.Ordinal);
        Assert.Contains("warfarin", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void One_person_keeps_one_invented_name()
    {
        const string text = "Sophia arrived at eight. Later Sophia called the doctor.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        string[] words = result.Text.Split([' ', '.', ','], StringSplitOptions.RemoveEmptyEntries);
        string first = words[0];
        Assert.Equal(2, words.Count(w => w == first));
    }

    [Fact]
    public void A_date_keeps_only_its_year()
    {
        const string text = "The appointment is 3/14/2026.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        Assert.Contains("2026", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("3/14", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_age_above_89_is_widened()
    {
        RedactionResult result = SiluetaEngine.CreateDefault().Redact("She is 94 years old.", Roster());

        Assert.Contains("90 or older", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("94", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_longest_span_wins_when_two_detectors_overlap()
    {
        // "jamileth" is a known family name and also sits inside an address the pattern pack matches.
        const string text = "Write to jamileth.v@example.com when you can.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        Assert.Contains("[EMAIL]", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_says_what_happened()
    {
        const string text = "Ellenor Vasques is 94 years old. Call 602-555-0147.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        Assert.Equal("shift-001", result.Manifest.RecordId);
        Assert.Equal("safe-harbor", result.Manifest.Policy);
        Assert.True(result.Manifest.ByKind[nameof(IdentifierKind.PatientName)] >= 1);
        Assert.True(result.Manifest.ByKind[nameof(IdentifierKind.Phone)] >= 1);
        Assert.Equal(1, result.Manifest.Subjects);
    }

    [Fact]
    public void The_vault_gives_each_subject_a_random_id()
    {
        var engine = SiluetaEngine.CreateDefault();

        engine.Redact("Ellenor Vasques and Jamileth were here.", Roster());

        Assert.Equal(2, engine.Vault.Count);
        string pseudonym = engine.Vault.PseudonymFor("patient-1");
        Assert.StartsWith("SIL-", pseudonym, StringComparison.Ordinal);
        Assert.True(engine.Vault.TryReidentify(pseudonym, out string subject));
        Assert.Equal("patient-1", subject);
    }

    [Fact]
    public void Nothing_known_means_nothing_invented()
    {
        const string text = "The patient rested well and ate half of her breakfast.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        Assert.Equal(text, result.Text);
        Assert.Empty(result.Applied);
    }
}
