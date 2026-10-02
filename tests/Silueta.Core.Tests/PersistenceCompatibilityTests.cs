using System.Text.Json;
using System.Text.Json.Nodes;
using Silueta.Core;

namespace Silueta.Core.Tests;

public sealed class PersistenceCompatibilityTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(Repo.Root,
        "tests", "Silueta.Core.Tests", "Fixtures", "preview.3", name)).TrimEnd('\r', '\n');

    [Fact]
    public void The_published_vault_keeps_codes_active_names_and_retired_names()
    {
        PseudonymVault vault = PseudonymVault.FromJson(Fixture("vault-v2.json"));
        Assert.Equal("SIL-00000000000000000000000000000001", vault.PseudonymFor("s-1"));
        Assert.Equal("Noa Bravo", vault.SurrogateFor("s-1"));
        Assert.Equal(["Ale Bravo"], vault.RetiredSurrogatesFor("s-1"));
        Assert.True(vault.TryFindSubjectBySurrogate("Ale Bravo", out string retiredOwner));
        Assert.Equal("s-1", retiredOwner);
        Assert.True(vault.TryReidentify("SIL-00000000000000000000000000000001", out string codeOwner));
        Assert.Equal("s-1", codeOwner);
        AssertBaselineFields(Fixture("vault-v2.json"), vault.ToJson());
    }

    [Fact]
    public void The_published_manifest_keeps_every_field_and_its_json_spelling()
    {
        RedactionManifest manifest = JsonSerializer.Deserialize(Fixture("manifest.json"),
            SiluetaJsonContext.Default.RedactionManifest)!;
        AssertBaselineFields(Fixture("manifest.json"),
            JsonSerializer.Serialize(manifest, SiluetaJsonContext.Default.RedactionManifest));
        Assert.Equal("fixture-r1", manifest.RecordId);
        Assert.Equal(0, manifest.ResidualSpans);
        Assert.Equal("2026-09-22 (record date)", manifest.AgeReference);
    }

    [Fact]
    public void The_published_lineage_preserves_content_identity_and_policy()
    {
        SiluetaLineage lineage = SiluetaLineage.FromJson(Fixture("lineage.json"));
        JsonNode fingerprints = JsonNode.Parse(Fixture("fingerprints.json"))!;
        Assert.Equal(fingerprints["lineage"]!.GetValue<string>(), lineage.Fingerprint);
        Assert.Equal(fingerprints["policy"]!.GetValue<string>(), lineage.Policy("review").Fingerprint);
        Assert.Equal(fingerprints["defaultPolicy"]!.GetValue<string>(), lineage.Policy("safe-harbor").Fingerprint);
        Assert.Equal(RedactionAction.Keep, lineage.Policy("review").ActionFor(IdentifierKind.StaffName));
        Assert.Equal("es-MX", lineage.Language);
        Assert.Equal(["calle", "avenida"], lineage.Lists["fixture-road"]);
    }

    [Fact]
    public void The_published_vault_and_lineage_still_redact_their_original_example()
    {
        SiluetaLineage lineage = SiluetaLineage.FromJson(Fixture("lineage.json"));
        PseudonymVault vault = PseudonymVault.FromJson(Fixture("vault-v2.json"), lineage.Pools);
        var context = new DeidentificationContext("fixture-r1") { RecordedOn = new DateOnly(2026, 9, 22) };
        context.AddPerson("s-1", "Sofia Reyes", IdentifierKind.PatientName);
        RedactionResult result = SiluetaEngine.FromLineage(lineage, vault).Redact(Fixture("input.txt"), context);
        Assert.Equal(Fixture("output.txt"), result.Text);
        Assert.Empty(result.Residue);
        Assert.Equal(3, result.Applied.Count);
        RedactionManifest previous = JsonSerializer.Deserialize(Fixture("manifest.json"),
            SiluetaJsonContext.Default.RedactionManifest)!;
        Assert.Equal(previous.InputSha256, result.Manifest.InputSha256);
        Assert.Equal(previous.OutputSha256, result.Manifest.OutputSha256);
    }

    private static void AssertBaselineFields(string expected, string actual)
    {
        JsonObject baseline = JsonNode.Parse(expected)!.AsObject();
        JsonObject current = JsonNode.Parse(actual)!.AsObject();
        // Additive root fields are allowed. Existing names, values and nested subject data survive.
        foreach ((string key, JsonNode? value) in baseline)
        {
            Assert.True(current.ContainsKey(key), $"Missing persisted field '{key}'.");
            Assert.True(JsonNode.DeepEquals(value, current[key]), $"Changed persisted field '{key}'.");
        }
    }
}
