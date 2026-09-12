using Silueta.Core;

namespace Silueta.Core.Tests;

public class SiluetaPolicyTests
{
    [Fact]
    public void The_default_policy_cannot_be_rewritten_from_outside()
    {
        // The attack this guards: a consumer flips one action on the shared default, and every engine
        // built afterwards keeps returning phone numbers under a policy still called "safe-harbor".
        // The manifest would say Safe Harbor ran. It did not.
        Assert.ThrowsAny<Exception>(() =>
            ((IDictionary<IdentifierKind, RedactionAction>)SiluetaPolicy.SafeHarbor.Actions)[IdentifierKind.Phone] =
                RedactionAction.Keep);

        RedactionResult result = SiluetaEngine.CreateDefault()
            .Redact("Call 602-555-0147.", new DeidentificationContext("policy-1"));

        Assert.DoesNotContain("602-555-0147", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_policy_takes_a_copy_of_the_actions_it_was_given()
    {
        var actions = new Dictionary<IdentifierKind, RedactionAction>
        {
            [IdentifierKind.Phone] = RedactionAction.Label,
        };

        var policy = new SiluetaPolicy { Actions = actions };
        actions[IdentifierKind.Phone] = RedactionAction.Keep;

        Assert.Equal(RedactionAction.Label, policy.ActionFor(IdentifierKind.Phone));
    }

    [Fact]
    public void Two_policies_that_differ_have_different_fingerprints()
    {
        var kept = new SiluetaPolicy
        {
            Actions = new Dictionary<IdentifierKind, RedactionAction>(SiluetaPolicy.SafeHarbor.Actions)
            {
                [IdentifierKind.Phone] = RedactionAction.Keep,
            },
        };

        Assert.NotEqual(SiluetaPolicy.SafeHarbor.Fingerprint, kept.Fingerprint);
    }

    [Fact]
    public void The_same_policy_fingerprints_the_same_way_twice()
    {
        var a = new SiluetaPolicy();
        var b = new SiluetaPolicy();

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(SiluetaPolicy.SafeHarbor.Fingerprint, a.Fingerprint);
    }

    [Fact]
    public void The_fingerprint_notices_a_changed_threshold()
    {
        var strict = new SiluetaPolicy { MinConfidence = 0.95 };

        Assert.NotEqual(SiluetaPolicy.SafeHarbor.Fingerprint, strict.Fingerprint);
    }

    [Fact]
    public void The_manifest_carries_the_fingerprint_of_the_policy_that_ran()
    {
        RedactionResult result = SiluetaEngine.CreateDefault()
            .Redact("Nothing to see.", new DeidentificationContext("policy-2"));

        Assert.Equal(SiluetaPolicy.SafeHarbor.Fingerprint, result.Manifest.PolicyFingerprint);
        Assert.NotEmpty(result.Manifest.PolicyFingerprint);
    }
}
