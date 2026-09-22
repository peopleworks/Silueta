using System.Text.Json;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The files under <c>examples/</c> are the ones a reader copies, so they are held to the same loader the
/// product uses. An example that has rotted is worse than no example: it teaches a shape that no longer
/// loads, and it does it to somebody who has no reason to doubt it yet.
/// </summary>
public class ExampleFilesTests
{
    private static readonly string Examples = Path.Combine(Repo.Root, "examples");

    public static TheoryData<string> Lineages()
    {
        var data = new TheoryData<string>();
        foreach (string file in Directory.EnumerateFiles(Examples, "lineage.*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Fact]
    public void The_examples_are_where_this_guard_is_looking()
    {
        // Without this, every check below passes the day somebody moves the folder.
        Assert.True(Directory.Exists(Examples), $"No examples at {Examples}.");
        Assert.NotEmpty(Lineages());
    }

    [Theory]
    [MemberData(nameof(Lineages))]
    public void Every_example_lineage_loads_and_keeps_its_rules(string name)
    {
        SiluetaLineage lineage = SiluetaLineage.Load(Path.Combine(Examples, name));

        Assert.NotEmpty(lineage.Name);
        Assert.NotEmpty(lineage.Labels);

        // A rule that names a list nobody declared is skipped rather than fatal, which is right for a file
        // written against a newer build — and wrong for an example, where it would be a rule that quietly
        // never fires in the file somebody just copied.
        PatternDetector detector = lineage.CreatePatternDetector();
        Assert.Empty(detector.RulesSkipped);
        Assert.Empty(lineage.Skipped);
        Assert.Equal(lineage.Patterns.Count, detector.RulesLoaded);
    }

    [Theory]
    [MemberData(nameof(Lineages))]
    public void Every_policy_an_example_declares_can_be_asked_for_by_name(string name)
    {
        SiluetaLineage lineage = SiluetaLineage.Load(Path.Combine(Examples, name));

        foreach (string policy in lineage.PolicyNames)
        {
            Assert.NotNull(lineage.Policy(policy));
        }
    }

    [Fact]
    public void The_example_roster_is_the_shape_the_command_line_reads()
    {
        using JsonDocument roster = JsonDocument.Parse(File.ReadAllText(Path.Combine(Examples, "roster.example.json")));

        Assert.NotEmpty(roster.RootElement.EnumerateArray());
        foreach (JsonElement entry in roster.RootElement.EnumerateArray())
        {
            Assert.True(IdentifierKindExtensions.TryParseName(entry.GetProperty("kind").GetString(), out _));
            Assert.NotEmpty(entry.GetProperty("value").GetString()!);
            Assert.NotEmpty(entry.GetProperty("subjectId").GetString()!);
        }
    }

    [Fact]
    public void An_example_lineage_redacts_a_line_of_its_own_country()
    {
        SiluetaLineage lineage = SiluetaLineage.Load(Path.Combine(Examples, "lineage.clinica-co.json"));
        var context = new DeidentificationContext("r-1");

        string text = SiluetaEngine.FromLineage(lineage, new PseudonymVault())
            .Redact("Vive en Medellín, Antioquia, en la Carrera 43 # 12-34, desde el 3 de marzo.", context).Text;

        Assert.Equal("Vive en [CIUDAD], Antioquia, en la [DIRECCIÓN], desde el [FECHA].", text);
    }
}
