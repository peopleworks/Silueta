using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The identifiers a company's transcripts are full of and the eighteen Safe Harbor identifiers never
/// mention: the organisation, the product, the client.
/// <para>
/// Two rules decide most of what happens to them, and each has to live in exactly one place. Which kinds
/// are <em>people</em> decides whether a roster entry is split into parts and which word lists an invented
/// name comes from; if the context and the vault ever give two answers to that question, a company is
/// treated as a person in one place and not in the other. And which kinds the policy names decides the
/// policy's fingerprint; a kind the table forgets is redacted under a rule the digest does not describe.
/// </para>
/// </summary>
public class BusinessKindTests
{
    [Theory]
    [InlineData(IdentifierKind.PatientName, true)]
    [InlineData(IdentifierKind.FamilyName, true)]
    [InlineData(IdentifierKind.StaffName, true)]
    [InlineData(IdentifierKind.OtherName, true)]
    [InlineData(IdentifierKind.ClientName, true)]
    [InlineData(IdentifierKind.Organization, false)]
    [InlineData(IdentifierKind.Product, false)]
    [InlineData(IdentifierKind.Phone, false)]
    [InlineData(IdentifierKind.Other, false)]
    public void There_is_one_answer_to_whether_a_kind_is_a_person(IdentifierKind kind, bool person)
    {
        // ClientName is a person on purpose. In home care — where this library started — the client IS
        // the patient, and a client company is an Organization. Written down here so that nobody writes
        // a lineage that sends a patient's name to a pool of company names.
        Assert.Equal(person, kind.IsPersonName());
    }

    [Fact]
    public void A_company_on_the_roster_does_not_turn_its_common_nouns_into_identifiers()
    {
        // A person is registered whole and part by part, because half the mentions in a transcript are
        // a first name alone. The same rule applied to "Acme Corporation" registered "Corporation" as
        // that client, and every unrelated corporation in the text became a surrogate. Measured before
        // this test existed: "Acme Corporation bought a corporation. The corporation is big." came back
        // as "Guadalupe Bravo bought a Guadalupe. The Guadalupe is big."
        var roster = new DeidentificationContext("biz-1")
            .AddPerson("client-7", "Acme Corporation", IdentifierKind.Organization);

        RedactionResult result = SiluetaEngine.CreateDefault()
            .Redact("Acme Corporation bought a corporation. The corporation is big.", roster);

        Assert.DoesNotContain("Acme", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("bought a corporation. The corporation is big.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_person_on_the_roster_is_still_found_by_a_first_name_alone()
    {
        var roster = new DeidentificationContext("biz-2")
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.ClientName);

        RedactionResult result = SiluetaEngine.CreateDefault().Redact("Eleanor rested well.", roster);

        Assert.DoesNotContain("Eleanor", result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IdentifierKind.Organization)]
    [InlineData(IdentifierKind.Product)]
    [InlineData(IdentifierKind.ClientName)]
    [InlineData(IdentifierKind.City)]
    [InlineData(IdentifierKind.State)]
    public void The_policy_names_every_new_kind_so_its_fingerprint_describes_the_rule_that_ran(IdentifierKind kind)
    {
        // ActionFor falls back to Label for a kind missing from the table, and the fingerprint walks the
        // table only. A build that added the kinds without adding the entries would have redacted them
        // under a rule the digest says nothing about, and printed the same digest as the build before.
        Assert.True(SiluetaPolicy.SafeHarbor.Actions.ContainsKey(kind), $"SafeHarbor does not name {kind}.");
    }

    [Fact]
    public void Safe_harbor_names_every_kind_there_is()
    {
        // The same reason as above, for whatever kind comes next: a kind added to the enum without a row here
        // is redacted under the fallback, which the fingerprint does not describe.
        Assert.All(Enum.GetValues<IdentifierKind>(), kind => Assert.True(SiluetaPolicy.SafeHarbor.Actions.ContainsKey(kind), $"SafeHarbor does not name {kind}."));
    }

    [Fact]
    public void Removing_more_than_Safe_Harbor_asks_is_a_new_version_of_the_policy()
    {
        // The name stays true — Safe Harbor is a floor, and removing a product name keeps nothing the
        // standard names — but the rules are not the rules a corpus labelled safe-harbor/0.1 was
        // redacted under, and that has to be readable without comparing hex. 0.3 added the city, which goes,
        // and the state, which stays: a corpus labelled 0.2 kept its cities because nothing looked for them.
        Assert.Equal("0.3", SiluetaPolicy.SafeHarbor.Version);
    }

    private static SiluetaLineage WithCompanies(string companyPools) => SiluetaLineage.FromJson($$"""
        {
          "lineage": "biz", "version": "1",
          "pools": { "given": ["Ale", "Noa"], "family": ["Bravo", "Toledo"], {{companyPools}} }
        }
        """);

    [Fact]
    public void Without_a_pool_of_company_names_a_company_is_labelled_and_never_given_a_persons_name()
    {
        // The built-in lineage ships no company names on purpose: an invented company name is very likely
        // a real company, and putting an uninvolved one inside a client's call is a different harm from an
        // invented person's name. So the library does not invent them for you. What it must not do instead
        // is the defect this whole slice exists to remove — reach for the only pool it has, which is
        // people, and turn "Acme Corporation" into "Ariel Bravo".
        var roster = new DeidentificationContext("biz-3")
            .AddPerson("client-7", "Acme Corporation", IdentifierKind.Organization);

        RedactionResult result = SiluetaEngine.CreateDefault().Redact("Acme Corporation called.", roster);

        Assert.Equal("[ORGANIZATION] called.", result.Text);

        // And it says so. A label where the policy asked for a surrogate is a decision the run made, and
        // a manifest that stayed quiet about it would describe a policy that did not happen.
        Assert.Contains(nameof(IdentifierKind.Organization), result.Manifest.SurrogatesUnavailable);
    }

    [Fact]
    public void A_lineage_that_brings_company_names_gets_a_company_back()
    {
        SiluetaLineage lineage = WithCompanies("""
            "company": ["Aurora Servicios", "Meridiano Logística"]
            """);
        var roster = new DeidentificationContext("biz-4")
            .AddPerson("client-7", "Acme Corporation", IdentifierKind.Organization);

        var engine = SiluetaEngine.FromLineage(lineage);
        RedactionResult result = engine.Redact("Acme Corporation called.", roster);

        Assert.True(engine.Vault.TryGetSurrogate("client-7", out string surrogate));
        Assert.Contains(surrogate, (string[])["Aurora Servicios", "Meridiano Logística"]);
        Assert.Equal($"{surrogate} called.", result.Text);
        Assert.Empty(result.Manifest.SurrogatesUnavailable);
    }

    [Fact]
    public void A_company_name_built_from_a_head_and_a_suffix_comes_apart_like_a_persons()
    {
        // Head × suffix reuses the minting the vault already has, and the fitter gives the right answer
        // for free: a one-word mention of the company gets the head alone.
        SiluetaLineage lineage = WithCompanies("""
            "company": ["Meridiano"], "companySuffix": ["Logística"]
            """);
        var roster = new DeidentificationContext("biz-5")
            .AddPerson("client-7", "Acme Corporation", IdentifierKind.Organization)
            .AddValue("Acme", IdentifierKind.Organization, "client-7");

        var engine = SiluetaEngine.FromLineage(lineage);
        RedactionResult result = engine.Redact("Acme Corporation called. Acme called again.", roster);

        Assert.Equal("Meridiano Logística called. Meridiano called again.", result.Text);
    }

    [Fact]
    public void A_product_draws_from_the_product_pool_and_not_from_the_company_one()
    {
        SiluetaLineage lineage = WithCompanies("""
            "company": ["Aurora Servicios"], "product": ["Nimbo"]
            """);
        var roster = new DeidentificationContext("biz-6")
            .AddPerson("client-7", "Acme Corporation", IdentifierKind.Organization)
            .AddPerson("product-3", "TurboFresh", IdentifierKind.Product);

        RedactionResult result = SiluetaEngine.FromLineage(lineage)
            .Redact("Acme Corporation complained about TurboFresh.", roster);

        Assert.Equal("Aurora Servicios complained about Nimbo.", result.Text);
    }

    [Fact]
    public void A_client_is_a_person_and_works_with_the_lists_that_ship()
    {
        var roster = new DeidentificationContext("biz-7")
            .AddPerson("client-1", "Eleanor Vasquez", IdentifierKind.ClientName);

        var engine = SiluetaEngine.CreateDefault();
        RedactionResult result = engine.Redact("Eleanor Vasquez renewed her plan.", roster);

        Assert.True(engine.Vault.TryGetSurrogate("client-1", out string surrogate));
        Assert.Contains(surrogate.Split(' ')[0], SiluetaLineage.Default.Pools.Given);
        Assert.Empty(result.Manifest.SurrogatesUnavailable);
    }

    [Fact]
    public void A_company_and_a_person_cannot_end_up_with_the_same_invented_name()
    {
        // One namespace, deliberately. Whoever holds the corpus holds a string, not a kind: nothing in a
        // redacted transcript says "this one was an Organization". Two subjects sharing one string would
        // leave one of them unreachable from the way back.
        SiluetaLineage lineage = WithCompanies("""
            "company": ["Aurora Servicios"]
            """);
        var vault = new PseudonymVault(lineage.Pools);

        string company = vault.SurrogateFor("client-7", IdentifierKind.Organization, static _ => false);

        Assert.Throws<ArgumentException>(() => vault.Assign("patient-1", company));
        Assert.True(vault.TryFindSubjectBySurrogate(company, out string back));
        Assert.Equal("client-7", back);
    }

    [Fact]
    public void Reminting_a_company_gives_it_another_company()
    {
        // Remint is the only way out of a name that stopped working. If it did not take the kind, the one
        // path that exists to fix a bad name would hand a company a person's.
        SiluetaLineage lineage = WithCompanies("""
            "company": ["Aurora Servicios", "Meridiano Logística"]
            """);
        var vault = new PseudonymVault(lineage.Pools);
        string first = vault.SurrogateFor("client-7", IdentifierKind.Organization, static _ => false);

        string second = vault.Remint("client-7", IdentifierKind.Organization, candidate => candidate == first);

        Assert.NotEqual(first, second);
        Assert.Contains(second, (string[])["Aurora Servicios", "Meridiano Logística"]);
    }

    [Fact]
    public void An_exhausted_company_pool_says_which_pool_ran_out()
    {
        // A person pool of 21 × 22 mints 462 names; a company pool of two mints two. "Widen the name lists"
        // is no help to someone whose person lists are fine.
        SiluetaLineage lineage = WithCompanies("""
            "company": ["Aurora Servicios", "Meridiano Logística"]
            """);
        var vault = new PseudonymVault(lineage.Pools);
        vault.SurrogateFor("a", IdentifierKind.Organization, static _ => false);
        vault.SurrogateFor("b", IdentifierKind.Organization, static _ => false);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            vault.SurrogateFor("c", IdentifierKind.Organization, static _ => false));

        Assert.Contains("company", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IdentifierKind.Organization, "[ORGANIZATION]")]
    [InlineData(IdentifierKind.Product, "[PRODUCT]")]
    [InlineData(IdentifierKind.ClientName, "[CLIENT]")]
    public void Each_new_kind_has_a_label_of_its_own_rather_than_one_shared_blank(IdentifierKind kind, string label)
    {
        // Without an entry in the built-in lineage all three fall through to [REMOVED], and "which kind
        // of thing was here" collapses into one token for exactly the corpus these kinds are for.
        Assert.Equal(label, SiluetaLineage.Default.LabelFor(kind));
    }
}
