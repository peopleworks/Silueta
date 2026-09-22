using System.Text.Json.Nodes;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Punto 2, first slice — what happens to each kind is the organisation's to write, in its lineage, under a
/// name of its own.
/// <para>
/// Pedro's principle, 22 September 2026: dates, ages, diagnoses, weights, vital signs, state and city are
/// statistics and must not be lost; what matters is not knowing whose they are. And, the same afternoon:
/// "por eso la idea de los diccionarios y la configuración dinámica". The lineage already carried the word
/// lists, the labels, the generalisations and the pattern rules. The table of actions was compiled in, so an
/// organisation that wanted to keep its dates had to fork the library.
/// </para>
/// <para>
/// Two rules hold the design together. Safe Harbor stays in code, once, as the legal floor — a second copy in
/// a data file would be the second copy of a rule — and its name is reserved: a file that could call itself
/// "safe-harbor" while keeping dates would put that name on every manifest over rules that are not it. And a
/// policy from a file is a set of departures from Safe Harbor, not a table from nothing: a kind it does not
/// name keeps Safe Harbor's action, so a mistake in the file falls back to removing more, not less. Every
/// departure is written into the manifest, because a policy is a decision and a compliance reader has to be
/// able to see it without the file.
/// </para>
/// </summary>
public class PolicyFromLineageTests
{
    private static SiluetaLineage WithPolicies(string policiesJson)
    {
        JsonNode lineage = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Repo.Root, "src", "Silueta.Core", "Lineage", "Lineages", "lineage.core.json")))!;
        lineage["lineage"] = "clinic-test";
        lineage["policies"] = JsonNode.Parse(policiesJson);
        return SiluetaLineage.FromJson(lineage.ToJsonString());
    }

    private static SiluetaLineage Statistics() => WithPolicies("""
        { "statistics": { "version": "1", "actions": { "Date": "Keep" } } }
        """);

    private static RedactionResult Run(SiluetaLineage lineage, SiluetaPolicy policy, string text) =>
        SiluetaEngine.FromLineage(lineage, new PseudonymVault()).Redact(text, new DeidentificationContext("r-1"), policy);

    [Fact]
    public void An_organisation_can_bring_a_policy_of_its_own_under_its_own_name()
    {
        SiluetaLineage lineage = Statistics();
        RedactionResult result = Run(lineage, lineage.Policy("statistics"), "The appointment is on 3/14/2026.");

        Assert.Contains("3/14/2026", result.Text, StringComparison.Ordinal);
        Assert.Equal("statistics", result.Manifest.Policy);
        Assert.Equal("1", result.Manifest.PolicyVersion);
        Assert.NotEqual(SiluetaPolicy.SafeHarbor.Fingerprint, result.Manifest.PolicyFingerprint);
    }

    [Fact]
    public void A_kind_the_policy_does_not_name_keeps_what_safe_harbor_does_with_it()
    {
        SiluetaLineage lineage = Statistics();
        RedactionResult result = Run(lineage, lineage.Policy("statistics"), "Call 602-555-0147 on 3/14/2026.");

        Assert.Contains("[PHONE]", result.Text, StringComparison.Ordinal);
        Assert.Equal(SiluetaPolicy.SafeHarbor.ActionFor(IdentifierKind.Phone), lineage.Policy("statistics").ActionFor(IdentifierKind.Phone));
    }

    [Fact]
    public void Every_departure_from_safe_harbor_is_written_into_the_manifest()
    {
        SiluetaLineage lineage = WithPolicies("""
            { "statistics": { "version": "1", "minConfidence": 0.6, "actions": { "Date": "Keep", "Phone": "Keep" } } }
            """);
        RedactionResult result = Run(lineage, lineage.Policy("statistics"), "Nothing to find here.");

        Assert.Equal(
            ["Date: Keep (Safe Harbor: YearOnly)", "minConfidence: 0.6 (Safe Harbor: 0.7)", "Phone: Keep (Safe Harbor: Label)"],
            result.Manifest.DeparturesFromSafeHarbor);
    }

    [Fact]
    public void A_safe_harbor_run_departs_from_nothing()
    {
        RedactionResult result = Run(SiluetaLineage.Default, SiluetaPolicy.SafeHarbor, "Call 602-555-0147.");

        Assert.Empty(result.Manifest.DeparturesFromSafeHarbor);
    }

    [Fact]
    public void Safe_harbor_is_always_available_and_no_file_may_redefine_it()
    {
        Assert.Same(SiluetaPolicy.SafeHarbor, Statistics().Policy("safe-harbor"));
        Assert.Same(SiluetaPolicy.SafeHarbor, SiluetaLineage.Default.Policy("safe-harbor"));

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => WithPolicies("""
            { "safe-harbor": { "version": "9", "actions": { "Date": "Keep" } } }
            """));
        Assert.Contains("reserved", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_safe_harbor_fingerprint_is_the_one_every_manifest_already_carries()
    {
        // Moving the table into a data-driven design must not change what the built-in policy digests to, or
        // every manifest ever written stops matching the policy it names.
        Assert.Equal("25fd6239682ded47", SiluetaPolicy.SafeHarbor.Fingerprint);
        Assert.Equal("bdd1fddb5f83b212", SiluetaLineage.Default.Fingerprint);
    }

    [Fact]
    public void An_action_this_build_does_not_know_is_refused_rather_than_guessed()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => WithPolicies("""
            { "statistics": { "version": "1", "actions": { "Date": "Kepp" } } }
            """));

        Assert.Contains("statistics", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Kepp", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Keep", refused.Message, StringComparison.Ordinal); // says what would have been valid
    }

    [Theory]
    [InlineData("Label, Keep")] // Enum.TryParse would have read this as Keep
    [InlineData("4")]           // and this as Keep too
    [InlineData("")]
    public void An_action_is_read_by_its_name_and_only_by_its_name(string written)
    {
        // E5 taught this for kinds: the framework's parser accepts numbers and comma-joined names, and for this
        // enum both of those land on Keep — the most permissive action there is.
        Assert.Throws<InvalidOperationException>(() => WithPolicies(
            "{ \"statistics\": { \"version\": \"1\", \"actions\": { \"Date\": \"" + written + "\" } } }"));
    }

    [Fact]
    public void A_kind_this_build_does_not_know_is_skipped_and_written_down()
    {
        // The same rule as labels and generalisations: a lineage written for a newer build names kinds this one
        // has never heard of. The kind falls back to Safe Harbor, which is the safe direction to fall.
        SiluetaLineage lineage = WithPolicies("""
            { "statistics": { "version": "1", "actions": { "Neighbourhood": "Keep" } } }
            """);

        Assert.Contains("policies.statistics.Neighbourhood", lineage.Skipped);
    }

    [Fact]
    public void A_policy_is_part_of_what_the_lineage_fingerprint_describes()
    {
        Assert.NotEqual(
            WithPolicies("""{ "statistics": { "version": "1", "actions": { "Date": "Keep" } } }""").Fingerprint,
            WithPolicies("""{ "statistics": { "version": "1", "actions": { "Date": "Label" } } }""").Fingerprint);
    }

    [Fact]
    public void Asking_for_a_policy_the_lineage_does_not_have_says_which_ones_it_does()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Statistics().Policy("research"));

        Assert.Contains("safe-harbor", refused.Message, StringComparison.Ordinal);
        Assert.Contains("statistics", refused.Message, StringComparison.Ordinal);
        Assert.Equal(["safe-harbor", "statistics"], Statistics().PolicyNames);
    }

    [Fact]
    public void A_run_that_is_not_safe_harbor_says_so_and_says_what_that_means_under_hipaa()
    {
        SiluetaLineage lineage = Statistics();
        var context = new DeidentificationContext("r-1");
        RedactionResult result = SiluetaEngine.FromLineage(lineage, new PseudonymVault())
            .Redact("The appointment is on 3/14/2026.", context, lineage.Policy("statistics"));

        string note = Assert.Single(Caveats.For(context, result), n => n.Contains("statistics", StringComparison.Ordinal));
        Assert.Contains("not Safe Harbor", note, StringComparison.Ordinal);
        Assert.Contains("Date: Keep", note, StringComparison.Ordinal);
        Assert.Contains("expert determination", note, StringComparison.OrdinalIgnoreCase);
    }
}
