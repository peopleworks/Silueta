using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The README opens on a claim wider than the clinic: put a transcript in front of an AI without handing
/// it the people in it. A company's transcripts are full of identifiers that are not people, and these
/// tests hold the prose about them to what the code does — deriving the gap from the code rather than
/// restating it, so that the day the gap closes they fail and force the paragraphs to be rewritten
/// instead of quietly becoming false.
/// <para>
/// This file used to pin the opposite: that <c>Organization</c>, <c>Product</c> and <c>ClientName</c> did
/// not exist and that a company on the roster came back as a person. It failed the moment the kinds were
/// added, which is what it was for. What it pins now is narrower and still true: the library invents no
/// company names of its own, and a company is matched only in the words the roster gave it.
/// </para>
/// </summary>
public class ScopeGapTests
{
    private static string RepoRoot => McpToolDocumentationTests.RepoRoot;

    [Fact]
    public void The_business_kinds_exist_and_the_built_in_lineage_invents_no_company_names()
    {
        foreach (string kind in (string[])["Organization", "Product", "ClientName"])
        {
            Assert.True(Enum.TryParse(kind, out IdentifierKind _), $"IdentifierKind has lost '{kind}'.");
        }

        // Deliberate, not missing: an invented company name is very likely a real company, and putting an
        // uninvolved one inside a client's call is a different harm from an invented person's name. The
        // day the built-in lineage ships company names, the README and SKILL.md paragraphs that say it
        // does not have to be rewritten — and so does the reasoning, which is the harder part.
        Assert.False(
            SiluetaLineage.Default.Pools.Has(IdentifierKind.Organization),
            "The built-in lineage now has company names. Rewrite the README (Status, and the lineage " +
            "section) and SKILL.md rule 6, then change this test by hand.");
        Assert.False(SiluetaLineage.Default.Pools.Has(IdentifierKind.Product));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SKILL.md")]
    public void The_documents_show_what_a_company_becomes_without_a_lineage_of_your_own(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        // The observable output, not a promise about it: whoever reads either document before running a
        // corpus of sales calls has to know the company names come back as this label.
        Assert.Contains("[ORGANIZATION]", text, StringComparison.Ordinal);
        Assert.Contains("ClientName", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_form_of_a_company_is_not_found_by_the_long_one()
    {
        // A company is matched whole, in exactly as many words as the roster gave it. "Acme Corp" is not
        // "Acme Corporation" to this matcher — Corp is seven edits from Corporation on a budget of two,
        // because abbreviation is truncation inside a word and not a sound substitution — and "Acme" alone
        // is not found at all. The fix today is a roster entry per form. When a later slice changes this,
        // the test fails, and the documents that tell people to add those entries have to change with it.
        var roster = new DeidentificationContext("gap-1")
            .AddPerson("client-7", "Acme Corporation", IdentifierKind.Organization);

        RedactionResult result = SiluetaEngine.CreateDefault()
            .Redact("Acme Corporation called. Acme Corp called again.", roster);

        Assert.StartsWith("[ORGANIZATION] called.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Acme Corp called again.", result.Text, StringComparison.Ordinal);

        foreach (string file in (string[])["README.md", "SKILL.md"])
        {
            Assert.Contains("Acme Corp", File.ReadAllText(Path.Combine(RepoRoot, file)), StringComparison.Ordinal);
        }
    }
}
