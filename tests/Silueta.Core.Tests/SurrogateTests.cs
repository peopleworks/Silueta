using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The invented names. A surrogate that can equal the name it replaces, or that two people share, or
/// that a second pass detects all over again, is not a redaction — it is a redaction-shaped thing.
/// </summary>
public class SurrogateTests
{
    [Fact]
    public void A_person_is_never_replaced_by_their_own_name()
    {
        // "Ale" is on the surrogate list. Under the old hash-of-subject-id scheme a real Ale could be
        // handed back "Ale", marked redacted, and counted as removed.
        var roster = new DeidentificationContext("surrogate-1")
            .AddPerson("patient-1", "Ale Espinal", IdentifierKind.PatientName);

        RedactionResult result = SiluetaEngine.CreateDefault().Redact("Ale Espinal rested well.", roster);

        Assert.DoesNotContain("Ale", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Espinal", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_surrogate_sounds_like_anyone_on_the_roster()
    {
        // Equality is not enough: the matcher is phonetic. A surrogate that merely *sounds* like a
        // roster name gets eaten by the next pass, which is how a redacted corpus starts moving.
        var vault = new PseudonymVault();
        string[] roster = ["Sofía Reyes", "Eleanor Vasquez", "Yamilet Vasquez", "Cruz Salazar"];

        string surrogate = vault.SurrogateFor("patient-1", roster);

        foreach (string name in roster)
        {
            foreach (Token part in Tokenizer.Tokenize(name))
            {
                foreach (Token invented in Tokenizer.Tokenize(surrogate))
                {
                    Assert.NotEqual(PhoneticKey.Compute(part.Text), PhoneticKey.Compute(invented.Text));
                }
            }
        }
    }

    [Fact]
    public void Two_hundred_subjects_get_two_hundred_different_surrogates()
    {
        var vault = new PseudonymVault();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 200; i++)
        {
            Assert.True(seen.Add(vault.SurrogateFor($"subject-{i}")), $"subject-{i} reused a surrogate.");
        }

        Assert.Equal(200, seen.Count);
    }

    [Fact]
    public void The_first_twenty_one_subjects_get_different_first_names_too()
    {
        // Half the mentions in a transcript are a first name alone. While there are unused given names
        // left, no two subjects may share one, or "Alex said" becomes ambiguous between two people.
        var vault = new PseudonymVault();

        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 21; i++)
        {
            Assert.True(given.Add(vault.SurrogateFor($"subject-{i}").Split(' ')[0]));
        }
    }

    [Fact]
    public void The_same_subject_keeps_the_same_surrogate()
    {
        var vault = new PseudonymVault();

        string first = vault.SurrogateFor("patient-1");

        Assert.Equal(first, vault.SurrogateFor("patient-1"));
        Assert.Equal(first, vault.SurrogateFor("patient-1", ["Eleanor Vasquez"]));
    }

    [Fact]
    public void Redacting_the_same_text_twice_gives_the_same_text()
    {
        const string text = "Ellenor Vasques slept well. Sophia signed the note.";
        var engine = SiluetaEngine.CreateDefault();

        string once = engine.Redact(text, Roster()).Text;
        string twice = engine.Redact(text, Roster()).Text;

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Redacting_an_already_redacted_transcript_changes_nothing()
    {
        // The real test of idempotence, and the one that catches a surrogate that sounds like a roster
        // name: feed the output back in. A de-identified corpus that keeps moving when it is reprocessed
        // is a corpus nobody can reproduce.
        const string text = "Ellenor Vasques slept well. Her daughter Jamileth called. Sophia signed it.";
        var engine = SiluetaEngine.CreateDefault();

        RedactionResult first = engine.Redact(text, Roster());
        RedactionResult second = engine.Redact(first.Text, Roster());

        Assert.Equal(first.Text, second.Text);
        Assert.Empty(second.Applied);
    }

    [Fact]
    public void A_vault_that_is_reloaded_keeps_every_assignment()
    {
        var vault = new PseudonymVault();
        string surrogate = vault.SurrogateFor("patient-1");
        string pseudonym = vault.PseudonymFor("patient-1");

        var reloaded = PseudonymVault.FromJson(vault.ToJson());

        Assert.Equal(surrogate, reloaded.SurrogateFor("patient-1"));
        Assert.Equal(pseudonym, reloaded.PseudonymFor("patient-1"));
        Assert.True(reloaded.TryReidentify(pseudonym, out string subject));
        Assert.Equal("patient-1", subject);
    }

    [Fact]
    public void A_reloaded_vault_does_not_hand_out_a_surrogate_it_already_used()
    {
        var vault = new PseudonymVault();
        string taken = vault.SurrogateFor("patient-1");

        var reloaded = PseudonymVault.FromJson(vault.ToJson());

        Assert.NotEqual(taken, reloaded.SurrogateFor("patient-2"));
    }

    [Fact]
    public void Re_identification_codes_are_long_enough_to_be_unguessable()
    {
        var vault = new PseudonymVault();

        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 500; i++)
        {
            string code = vault.PseudonymFor($"subject-{i}");

            // "SIL-" plus at least 128 bits of hex. Sixty-four bits is inside birthday range for a
            // corpus of any size, and a re-identification code that collides merges two people.
            Assert.StartsWith("SIL-", code, StringComparison.Ordinal);
            Assert.True(code.Length - 4 >= 32, $"{code} carries only {(code.Length - 4) * 4} bits.");
            Assert.True(codes.Add(code), "a code was minted twice.");
        }
    }

    [Fact]
    public void A_vault_written_to_disk_can_be_read_back()
    {
        string path = Path.Combine(Path.GetTempPath(), $"silueta-vault-{Guid.NewGuid():N}.json");
        try
        {
            var vault = new PseudonymVault();
            string surrogate = vault.SurrogateFor("patient-1");
            vault.SaveTo(path);

            // Second run, same file: the assignments from the first must survive.
            PseudonymVault reopened = PseudonymVault.LoadOrCreate(path);
            reopened.SurrogateFor("patient-2");
            reopened.SaveTo(path);

            PseudonymVault third = PseudonymVault.LoadOrCreate(path);
            Assert.Equal(surrogate, third.SurrogateFor("patient-1"));
            Assert.Equal(2, third.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loading_a_vault_that_is_not_there_starts_an_empty_one()
    {
        PseudonymVault vault = PseudonymVault.LoadOrCreate(
            Path.Combine(Path.GetTempPath(), $"silueta-absent-{Guid.NewGuid():N}.json"));

        Assert.Equal(0, vault.Count);
    }

    private static DeidentificationContext Roster() => new DeidentificationContext("shift-001")
        .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)
        .AddPerson("family-1", "Yamilet Vasquez", IdentifierKind.FamilyName)
        .AddPerson("staff-1", "Sofía Reyes", IdentifierKind.StaffName);
}
