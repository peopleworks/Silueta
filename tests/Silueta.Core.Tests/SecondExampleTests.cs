using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The README's second worked example, held to what the library actually does with it.
/// <para>
/// The first one is checked in CI against <c>silueta demo</c>. This one shows the parts that came later —
/// a street address, a town before a state, a ZIP against the census, a date of birth that makes somebody
/// 90, an e-mail address said out loud — and the same rule applies to it: a block of output in a README is
/// a copy of a result, and a copy that nothing checks is a claim that goes quietly stale.
/// </para>
/// </summary>
public class SecondExampleTests
{
    private const string Transcript = """
        Discharge summary. Date of birth: 3/14/1931. Eleanor Vasquez moved to 412 West Palm Lane,
        Mesa, AZ 85201, and her sister still lives in Prescott. The nurse, Sofía Reyes, said the
        pharmacy on Cactus Road delivers on Tuesdays and that her e-mail is
        eleanor dot vasquez at example dot com. Blood pressure 138 over 82, pain 4 out of 10.
        """;

    private static readonly DateOnly Recorded = new(2026, 9, 22);

    private static DeidentificationContext Roster() =>
        new DeidentificationContext("demo-002") { RecordedOn = Recorded }
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)
            .AddPerson("staff-1", "Sofía Reyes", IdentifierKind.StaffName);

    // Pre-assigned, as the CLI's demo does it: invented names are minted at random and remembered in the
    // vault, so a real run cannot print the same thing twice — which is right, and no use for a README.
    private static PseudonymVault Vault(SiluetaLineage lineage) =>
        new PseudonymVault(lineage.Pools).Assign("patient-1", "Ale Espinal").Assign("staff-1", "Yael Bravo");

    private static string Block(string marker)
    {
        string readme = File.ReadAllText(Path.Combine(Repo.Root, "README.md"));
        Match block = new Regex($"<!-- {marker}:start.*?```\n(?<body>.*?)```", RegexOptions.Singleline).Match(readme);

        Assert.True(block.Success, $"The README has no {marker} block for this test to check.");
        return block.Groups["body"].Value.Replace("\r\n", "\n").TrimEnd('\n');
    }

    [Fact]
    public void The_README_shows_what_this_build_does_with_places_a_birth_date_and_a_dictated_address()
    {
        string text = SiluetaEngine.FromLineage(SiluetaLineage.Default, Vault(SiluetaLineage.Default))
            .Redact(Transcript, Roster()).Text;

        Assert.Equal(Block("places-output"), text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void And_what_one_more_line_in_a_lineage_does_to_the_town_it_misses()
    {
        JsonNode file = JsonNode.Parse(SiluetaLineage.DefaultJson)!;
        file["lineage"] = "clinic-example";
        file["version"] = "1";
        file["values"] = new JsonObject { [nameof(IdentifierKind.City)] = new JsonArray("Prescott") };
        SiluetaLineage lineage = SiluetaLineage.FromJson(file.ToJsonString());

        string text = SiluetaEngine.FromLineage(lineage, Vault(lineage)).Redact(Transcript, Roster()).Text;

        Assert.Equal(Block("places-values-output"), text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_policy_that_keeps_places_keeps_them_and_lists_every_departure()
    {
        JsonNode file = JsonNode.Parse(SiluetaLineage.DefaultJson)!;
        file["lineage"] = "clinic-example";
        file["version"] = "1";
        file["policies"] = new JsonObject
        {
            ["statistics"] = new JsonObject
            {
                ["version"] = "1",
                ["actions"] = new JsonObject
                {
                    [nameof(IdentifierKind.Date)] = nameof(RedactionAction.Keep),
                    [nameof(IdentifierKind.City)] = nameof(RedactionAction.Keep),
                    [nameof(IdentifierKind.PostalCode)] = nameof(RedactionAction.Keep),
                },
            },
        };

        SiluetaLineage lineage = SiluetaLineage.FromJson(file.ToJsonString());
        RedactionResult result = SiluetaEngine.FromLineage(lineage, Vault(lineage))
            .Redact(Transcript, Roster(), lineage.Policy("statistics"));

        Assert.Contains("Mesa, AZ 85201", result.Text, StringComparison.Ordinal);
        Assert.Equal(
            ["City: Keep (Safe Harbor: Label)", "Date: Keep (Safe Harbor: YearOnly)", "PostalCode: Keep (Safe Harbor: Generalize)"],
            result.Manifest.DeparturesFromSafeHarbor);

        // The street address is not a departure anybody made: an exact address is a leak under every policy
        // here, and the README says so in Pedro's words.
        Assert.DoesNotContain("412 West Palm Lane", result.Text, StringComparison.Ordinal);
    }
}
