using System.Text.Json.Nodes;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Punto 2, third slice — a city is removed and a state is kept, as Safe Harbor says, and an organisation's own
/// policy can keep both.
/// <para>
/// Pedro, 22 September 2026: state, city and ZIP are statistics; an exact address is a leak. Safe Harbor removes
/// "all geographic subdivisions smaller than a state" — 45 CFR § 164.514(b)(2)(i)(B) — so a city goes and the
/// state may stay. Neither was a kind before: a city survived because no rule looked for it, and a state
/// survived for the same reason, which is right by accident and says nothing in a manifest.
/// </para>
/// <para>
/// Two ways to find a city, chosen with Pedro. A rule anchored on the state that follows it — "Flagstaff,
/// Arizona", "Mesa, AZ 85201" — because a capitalised word before a state is a place far more often than it is
/// anything else, where a capitalised word alone is usually a person. And the organisation's own list, in its
/// lineage: Navi knows the towns it serves, and a list is found in a transcript with no capitals, where the rule
/// cannot see anything. The states themselves are one list, compiled in, that every rule names instead of
/// spelling it out.
/// </para>
/// </summary>
public class PlacesTests
{
    private static RedactionResult Run(
        string text, SiluetaPolicy? policy = null, SiluetaLineage? lineage = null, DeidentificationContext? context = null) =>
        SiluetaEngine.FromLineage(lineage ?? SiluetaLineage.Default, new PseudonymVault())
            .Redact(text, context ?? new DeidentificationContext("r-1"), policy ?? SiluetaPolicy.SafeHarbor);

    private static SiluetaLineage With(string key, string json)
    {
        JsonNode lineage = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Repo.Root, "src", "Silueta.Core", "Lineage", "Lineages", "lineage.core.json")))!;
        lineage["lineage"] = "clinic-test";
        lineage[key] = JsonNode.Parse(json);
        return SiluetaLineage.FromJson(lineage.ToJsonString());
    }

    private static (SiluetaLineage Lineage, SiluetaPolicy Policy) Policy(string actions)
    {
        SiluetaLineage lineage = With("policies", "{ \"p\": { \"version\": \"1\", \"actions\": " + actions + " } }");
        return (lineage, lineage.Policy("p"));
    }

    [Theory]
    [InlineData("She moved to Flagstaff, Arizona last year.", "She moved to [CITY], Arizona last year.")]
    [InlineData("Send it to Mesa, AZ 85201.", "Send it to [CITY], AZ 852XX.")]
    [InlineData("Flagstaff AZ 86001", "[CITY] AZ 860XX")]
    [InlineData("They drove from Salt Lake City, Utah.", "They drove from [CITY], Utah.")]
    [InlineData("Vive en Las Cruces, Nuevo México.", "Vive en [CITY], Nuevo México.")]
    [InlineData("They flew to Washington, DC for the surgery.", "They flew to [CITY], DC for the surgery.")]
    [InlineData("Her son lives in New York, NY now.", "Her son lives in [CITY], NY now.")]
    public void Under_safe_harbor_the_city_before_a_state_goes_and_the_state_stays(string text, string expected)
    {
        Assert.Equal(expected, Run(text).Text);
    }

    [Theory]
    [InlineData("Arizona is hot in July.")]
    [InlineData("Yes, Arizona.")]
    [InlineData("We met Maria. Arizona was lovely.")]
    public void A_state_with_no_place_before_it_is_not_a_city(string text)
    {
        RedactionResult result = Run(text);

        Assert.Equal(text, result.Text);
        Assert.DoesNotContain(nameof(IdentifierKind.City), result.Manifest.ByKind.Keys);
    }

    [Theory]
    [InlineData("She grew up in Georgia.", "She grew up in [STATE].")]
    [InlineData("Nació en Carolina del Norte.", "Nació en [STATE].")]
    [InlineData("Send it to Mesa, AZ 85201.", "Send it to [CITY], [STATE] 852XX.")]
    public void A_state_is_found_even_though_safe_harbor_keeps_it(string text, string expected)
    {
        // Found so that a stricter organisation can remove it and the manifest can count it. Under Safe Harbor
        // the reading is dropped before anything is replaced, so finding it changes nothing there.
        (SiluetaLineage lineage, SiluetaPolicy policy) = Policy("""{ "State": "Label" }""");

        Assert.Equal(expected, Run(text, policy, lineage).Text);
    }

    [Theory]
    [InlineData("OK, I will call her.")]
    [InlineData("Put it in the box, OR the drawer.")]
    [InlineData("Tell me IN detail.")]
    public void A_two_letter_code_is_a_state_only_after_a_place_or_before_a_zip(string text)
    {
        // OK, OR, IN, ME, HI, LA, DE, CO: the codes are words in both languages. Read loosely, a policy that
        // removes states would take them out of every sentence.
        (SiluetaLineage lineage, SiluetaPolicy policy) = Policy("""{ "State": "Label" }""");

        Assert.Equal(text, Run(text, policy, lineage).Text);
    }

    [Fact]
    public void A_name_on_the_roster_that_is_also_a_state_is_still_removed()
    {
        // The state reading is one the policy keeps; a kept reading never outranks one that removes. Otherwise
        // a patient called Georgia would be kept, because Georgia is also a state.
        var context = new DeidentificationContext("r-1").AddPerson("patient-1", "Georgia Lopez", IdentifierKind.PatientName);

        Assert.DoesNotContain("Georgia", Run("Georgia said her knee hurts.", context: context).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_city_in_a_transcript_with_no_capitals_is_found_only_from_the_organisations_list()
    {
        // A recogniser that writes everything in lower case gives the rule nothing to anchor on: "phoenix" is
        // not a capitalised word. Pinned so that the day this changes, it changes on purpose.
        Assert.Equal("i live in phoenix arizona", Run("i live in phoenix arizona").Text);
    }

    [Fact]
    public void An_organisation_names_the_cities_it_serves_in_its_lineage()
    {
        SiluetaLineage lineage = With("values", """{ "City": ["Scottsdale", "Queen Creek"] }""");

        Assert.Equal(
            "we saw her in [CITY] on monday, then [CITY]",
            Run("we saw her in scotsdale on monday, then queen creek", lineage: lineage).Text);
    }

    [Fact]
    public void A_person_cannot_be_a_value_every_record_shares()
    {
        // A person needs a subject so that the same person gets the same invented name; a list in the lineage
        // has none. People belong on each record's roster.
        SiluetaLineage lineage = With("values", """{ "PatientName": ["Ana"], "Neighbourhood": ["Arcadia"], "City": ["Tempe"] }""");

        Assert.Contains("values.PatientName", lineage.Skipped);
        Assert.Contains("values.Neighbourhood", lineage.Skipped);
        Assert.Equal("Ana went to [CITY].", Run("Ana went to Tempe.", lineage: lineage).Text);
    }

    [Fact]
    public void The_lineages_values_are_part_of_what_its_fingerprint_describes()
    {
        Assert.NotEqual(
            With("values", """{ "City": ["Tempe"] }""").Fingerprint,
            With("values", """{ "City": ["Mesa"] }""").Fingerprint);
    }

    [Fact]
    public void An_organisations_policy_can_keep_the_city()
    {
        (SiluetaLineage lineage, SiluetaPolicy policy) = Policy("""{ "City": "Keep" }""");
        RedactionResult result = Run("She moved to Flagstaff, Arizona last year.", policy, lineage);

        Assert.Equal("She moved to Flagstaff, Arizona last year.", result.Text);
        Assert.Contains("City: Keep (Safe Harbor: Label)", result.Manifest.DeparturesFromSafeHarbor);
    }

    [Fact]
    public void Safe_harbor_removes_the_city_keeps_the_state_and_says_which_version_does()
    {
        Assert.Equal(RedactionAction.Label, SiluetaPolicy.SafeHarbor.ActionFor(IdentifierKind.City));
        Assert.Equal(RedactionAction.Keep, SiluetaPolicy.SafeHarbor.ActionFor(IdentifierKind.State));
        Assert.Equal("0.3", SiluetaPolicy.SafeHarbor.Version);
        Assert.Contains(nameof(IdentifierKind.State), Run("Nothing here.").Manifest.KeptKinds);
    }

    [Fact]
    public void A_label_in_title_case_is_not_read_back_as_a_city()
    {
        // "[Ciudad], Arizona" is a capitalised word before a state. Read back as a city, every Spanish-speaking
        // lineage's output would report residue that is only its own label.
        SiluetaLineage lineage = With("labels", """{ "City": "[Ciudad]" }""");
        RedactionResult result = Run("She moved to Flagstaff, Arizona last year.", lineage: lineage);

        Assert.Equal("She moved to [Ciudad], Arizona last year.", result.Text);
        Assert.Equal(0, result.Manifest.ResidualSpans);
    }

    [Fact]
    public void Every_kind_is_listed_where_the_tools_tell_a_person_what_kinds_there_are()
    {
        // The command line's usage and the MCP server's roster parameter each spell the kinds out by hand, for a
        // person to read. A kind added to the enum and not to them is a kind nobody is told exists.
        string cli = File.ReadAllText(Path.Combine(Repo.Root, "src", "Silueta.Cli", "Commands.cs"));
        string mcp = File.ReadAllText(Path.Combine(Repo.Root, "src", "Silueta.Mcp", "Tools", "RedactionTools.cs"));
        string cliKinds = cli[cli.IndexOf("Kinds: PatientName", StringComparison.Ordinal)..];
        string mcpKinds = mcp[mcp.IndexOf("Kinds: PatientName", StringComparison.Ordinal)..];

        Assert.All(Enum.GetNames<IdentifierKind>(), kind =>
        {
            Assert.Contains(kind, cliKinds[..cliKinds.IndexOf("\"\"\"", StringComparison.Ordinal)], StringComparison.Ordinal);
            Assert.Contains(kind, mcpKinds[..mcpKinds.IndexOf("\")]", StringComparison.Ordinal)], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Every_state_has_a_code_and_a_name_and_no_code_twice()
    {
        JsonNode lists = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Repo.Root, "src", "Silueta.Core", "Detectors", "Packs", "lists.core.json")))!;
        JsonArray states = lists["usStates"]!.AsArray();

        // Fifty, the District of Columbia and Puerto Rico.
        Assert.Equal(52, states.Count);
        Assert.Equal(52, states.Select(s => (string)s!["code"]!).Distinct().Count());
        Assert.All(states, s => Assert.NotEmpty(s!["names"]!.AsArray()));
        Assert.Equal(52, PatternLists.Expand("{{us-state-code}}").Split('|').Length);
    }

    [Fact]
    public void A_rule_that_names_a_list_this_build_does_not_have_is_skipped_and_written_down()
    {
        var detector = new PatternDetector([new PatternRule { Id = "county", Kind = "City", Regex = "{{us-county}}" }]);

        Assert.Equal(0, detector.RulesLoaded);
        Assert.Contains("county", detector.RulesSkipped);
    }

    [Fact]
    public void The_pattern_fingerprint_is_taken_over_the_lists_as_they_ran()
    {
        // Over the expansion, not over the placeholder: a list that changes changes what the rules find, and a
        // digest of "{{us-state}}" would not notice.
        var named = new PatternDetector([new PatternRule { Id = "s", Kind = "State", Regex = @"\b{{us-state}}\b" }]);
        var spelled = new PatternDetector([new PatternRule { Id = "s", Kind = "State", Regex = PatternLists.Expand(@"\b{{us-state}}\b") }]);

        Assert.Equal(spelled.Fingerprint, named.Fingerprint);
    }
}
