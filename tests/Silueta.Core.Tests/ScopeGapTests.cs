using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The README now opens on a claim wider than the clinic: put a transcript in front of an AI without
/// handing it the people in it. A company's transcripts are full of identifiers this library has no kind
/// for — the client, the product, the account — and a roster entry for one of those is replaced by a
/// <em>person's</em> name, because the only pool there is is a pool of people.
/// <para>
/// So the widening has to carry its own gap statement, in the two documents someone would read on their
/// own: the README, and the skill an agent acts on without asking. These tests derive the gap from the
/// enum rather than restating it, so the day the kinds exist they fail and force the paragraphs to be
/// rewritten instead of quietly becoming false. That is the failure this project keeps finding: a rule
/// written in one place while the truth lives in another.
/// </para>
/// </summary>
public class ScopeGapTests
{
    private static readonly string[] MissingKinds = ["Organization", "Product", "ClientName"];

    private static string RepoRoot => McpToolDocumentationTests.RepoRoot;

    [Fact]
    public void The_kinds_a_business_transcript_needs_still_do_not_exist()
    {
        foreach (string kind in MissingKinds)
        {
            Assert.False(
                Enum.TryParse(kind, ignoreCase: true, out IdentifierKind _),
                $"IdentifierKind now has '{kind}'. Rewrite the gap paragraphs in README.md (Status) and " +
                "SKILL.md, then delete or narrow this test by hand.");
        }
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SKILL.md")]
    public void The_documents_that_widened_the_claim_also_name_the_gap(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        foreach (string kind in MissingKinds)
        {
            Assert.True(
                text.Contains(kind, StringComparison.OrdinalIgnoreCase),
                $"{relativePath} does not say that '{kind}' is not an identifier kind, while the library " +
                "claims to de-identify text so it can be analysed by an AI. A reader with a corpus of " +
                "sales calls would take the claim at face value.");
        }
    }

    [Fact]
    public void A_company_on_the_roster_is_replaced_by_a_person_which_is_the_gap_itself()
    {
        // Not a wish: the behaviour the paragraphs describe. A company name IS found and removed — the
        // identifier does go — and what takes its place has the wrong shape, which is the half that
        // makes the sentence stop being worth analysing.
        var roster = new DeidentificationContext("scope-1")
            .AddPerson("client-7", "Acme Corporation", IdentifierKind.OtherName);

        var engine = SiluetaEngine.CreateDefault();
        RedactionResult result = engine.Redact("Acme Corporation called about the delay.", roster);

        // The identifier does go. That half works.
        Assert.DoesNotContain("Acme", result.Text, StringComparison.OrdinalIgnoreCase);

        Assert.True(engine.Vault.TryGetSurrogate("client-7", out string replacement));
        Assert.DoesNotContain("Corp", replacement, StringComparison.OrdinalIgnoreCase);

        // And it came out of the pool of people: the name a company was given is now taken, and a
        // patient cannot be assigned it. One namespace, one pool, no shape of its own for an
        // organisation — which is the gap the README and SKILL.md describe in prose.
        Assert.Throws<ArgumentException>(() => new PseudonymVault()
            .Assign("client-7", replacement)
            .Assign("patient-9", replacement));
    }
}
