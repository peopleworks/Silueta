using System.Security.Cryptography;
using System.Text;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The manifest is the artefact an expert determination rests on, and it travels with the corpus. A
/// reviewer holding both has to be able to say "this manifest describes this file" — which it could not,
/// because the manifest was bound to nothing at all.
/// </summary>
public class ManifestTests
{
    private static DeidentificationContext Roster() => new DeidentificationContext("shift-009")
        .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void It_names_the_text_that_went_in_and_the_text_that_came_out()
    {
        const string text = "Ellenor Vasques rested well. Call 602-555-0147.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        Assert.Equal(Sha256(text), result.Manifest.InputSha256);
        Assert.Equal(Sha256(result.Text), result.Manifest.OutputSha256);
    }

    [Fact]
    public void Two_different_transcripts_do_not_share_a_manifest()
    {
        var engine = SiluetaEngine.CreateDefault();

        RedactionManifest a = engine.Redact("Ellenor Vasques rested.", Roster()).Manifest;
        RedactionManifest b = engine.Redact("Ellenor Vasques rested well.", Roster()).Manifest;

        Assert.NotEqual(a.InputSha256, b.InputSha256);
    }

    [Fact]
    public void It_fingerprints_the_rules_that_did_the_finding_not_only_the_policy()
    {
        // The policy was fingerprinted; the pattern pack that does the finding was not. Two corpora
        // could carry the same policy fingerprint and have been searched with different rules.
        RedactionResult result = SiluetaEngine.CreateDefault().Redact("Nothing here.", Roster());

        Assert.True(result.Manifest.DetectorFingerprints.TryGetValue("pattern", out string? fingerprint));
        Assert.NotEmpty(fingerprint!);

        var different = new SiluetaEngine([new PatternDetector([
            new PatternRule { Id = "only-email", Kind = "Email", Regex = @"\S+@\S+" },
        ])]);

        Assert.NotEqual(
            fingerprint,
            different.Redact("Nothing here.", Roster()).Manifest.DetectorFingerprints["pattern"]);
    }

    [Fact]
    public void A_rule_the_build_could_not_load_is_recorded_rather_than_swallowed()
    {
        // A pack naming a kind this build does not know is skipped so a newer pack cannot crash a run.
        // Silently, though, the difference between "that rule found nothing" and "that rule never ran"
        // was a missing dictionary key.
        var detector = new PatternDetector([
            new PatternRule { Id = "phone-typo", Kind = "Phonee", Regex = @"\d{10}" },
            new PatternRule { Id = "email", Kind = "Email", Regex = @"\S+@\S+" },
        ]);

        RedactionResult result = new SiluetaEngine([detector]).Redact("Call 6025550147.", Roster());

        Assert.Equal(1, result.Manifest.DetectorRulesLoaded["pattern"]);
        Assert.Contains("phone-typo", result.Manifest.DetectorRulesSkipped["pattern"]);
        Assert.DoesNotContain("6025550147", string.Join(" ", result.Manifest.DetectorRulesSkipped["pattern"]));
    }

    [Fact]
    public void It_says_which_kinds_the_policy_was_told_to_leave_alone()
    {
        // A corpus redacted with StaffName = Keep produced counts identical to a transcript with no
        // staff in it. The fingerprint told them apart; the counts actively misled.
        var lenient = new SiluetaPolicy
        {
            Actions = new Dictionary<IdentifierKind, RedactionAction>(SiluetaPolicy.SafeHarbor.Actions)
            {
                [IdentifierKind.StaffName] = RedactionAction.Keep,
            },
        };

        RedactionResult result = SiluetaEngine.CreateDefault().Redact("Nothing here.", Roster(), lenient);

        Assert.Contains(nameof(IdentifierKind.StaffName), result.Manifest.KeptKinds);
        Assert.Empty(SiluetaEngine.CreateDefault().Redact("Nothing here.", Roster()).Manifest.KeptKinds);
    }
}
