namespace Silueta.Core.Tests;

/// <summary>
/// SKILL.md is not documentation. It is an instruction set an agent loads and acts on, so a claim that
/// drifts out of date here does not merely mislead a reader — it changes what a machine does with
/// somebody's clinical transcript. These tests pin the parts that must not quietly go missing.
/// </summary>
public class SkillDocumentTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string Skill = File.ReadAllText(Path.Combine(RepoRoot, "SKILL.md"));

    [Fact]
    public void It_has_the_front_matter_every_installer_reads()
    {
        Assert.StartsWith("---", Skill, StringComparison.Ordinal);

        int close = Skill.IndexOf("\n---", 3, StringComparison.Ordinal);
        Assert.True(close > 0, "the YAML front matter is not closed.");

        string frontMatter = Skill[..close];
        Assert.Contains("name: silueta", frontMatter, StringComparison.Ordinal);
        Assert.Contains("description:", frontMatter, StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_says_when_to_use_it_without_promising_a_result()
    {
        int close = Skill.IndexOf("\n---", 3, StringComparison.Ordinal);
        string frontMatter = Skill[..close];

        // A skill description is matched against the user's request, so it has to name the situations.
        foreach (string trigger in (string[])["transcript", "de-identif", "Silueta"])
        {
            Assert.Contains(trigger, frontMatter, StringComparison.OrdinalIgnoreCase);
        }

        // And it must carry the disclaimer into the one place a model always reads.
        Assert.Contains("leak rate", frontMatter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_rule_that_matters_most_is_still_in_it()
    {
        // Reading the transcript into the conversation is the leak. If this ever drops out, the skill
        // is a redaction tool with the failure mode it was written to prevent.
        Assert.Contains("is the leak", Skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redact_transcript", Skill, StringComparison.Ordinal);
    }

    [Fact]
    public void It_refuses_re_identification_in_so_many_words()
    {
        Assert.Contains("Never re-identify", Skill, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_names_what_is_known_to_survive()
    {
        foreach (string gap in (string[])["Ellie", "Rays", "0.40", "postal code"])
        {
            Assert.Contains(gap, Skill, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void It_insists_on_opaque_ids()
    {
        Assert.Contains("subjectId", Skill, StringComparison.Ordinal);
        Assert.Contains("opaque", Skill, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_plugin_manifests_agree_with_the_skill_name()
    {
        foreach (string manifest in (string[])["marketplace.json", "plugin.json"])
        {
            string json = File.ReadAllText(Path.Combine(RepoRoot, ".claude-plugin", manifest));
            Assert.Contains("\"name\": \"silueta\"", json, StringComparison.Ordinal);
            Assert.Contains("peopleworks/Silueta", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void It_follows_its_own_rule_and_quotes_no_transcript()
    {
        // The skill tells an agent never to paste transcript content. A skill that demonstrates itself
        // with a paragraph of a shift note would be teaching the opposite by example. The roster sample
        // is fine — those names are the repository's own invented demo people.
        Assert.DoesNotContain("Blood pressure", Skill, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("warfarin", Skill, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Silueta.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
