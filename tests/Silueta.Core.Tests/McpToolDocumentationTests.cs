using System.Text.RegularExpressions;

namespace Silueta.Core.Tests;

/// <summary>
/// Three files describe the MCP server's tools by hand: the repository README, the server package's own
/// README, and SKILL.md — which is the one an agent actually reads and acts on.
/// <para>
/// In the sibling project this guard was written for, a tool shipped and its row was never added to the
/// table. Two public directory listings then copied the stale table faithfully and were wrong in the
/// same way, weeks later, in someone else's repository where there is no guard at all. Here the stakes
/// are higher than a wrong table: SKILL.md tells an agent which tool keeps identified text out of the
/// conversation. A tool missing from it is a tool nobody routes through.
/// </para>
/// <para>
/// So this reads the server's own attributes and requires all three files to agree with them. Adding a
/// tool without documenting it fails here, by name.
/// </para>
/// </summary>
public partial class McpToolDocumentationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly IReadOnlyList<string> Tools = ReadToolNames();

    [Fact]
    public void The_server_declares_the_tools_this_guard_expects()
    {
        // A sanity check on the guard itself: if the regex stops matching, every test below would pass
        // vacuously against an empty list.
        Assert.Equal(4, Tools.Count);
        Assert.Contains("redact_transcript", Tools);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SKILL.md")]
    [InlineData("skill/README.md")]
    [InlineData("src/Silueta.Mcp/README.md")]
    public void Every_tool_the_server_exposes_is_named_in_the_documentation(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        foreach (string tool in Tools)
        {
            Assert.True(
                text.Contains(tool, StringComparison.Ordinal),
                $"{relativePath} does not mention the MCP tool '{tool}'.");
        }
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SKILL.md")]
    [InlineData("skill/README.md")]
    [InlineData("src/Silueta.Mcp/README.md")]
    public void The_documentation_names_no_tool_the_server_does_not_have(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        // Anything shaped like a tool name in backticks. A doc that promises `reidentify_subject`
        // is worse than a doc missing a row.
        foreach (Match match in BacktickedIdentifier().Matches(text))
        {
            string candidate = match.Groups["name"].Value;
            if (KnownNonTools.Contains(candidate))
            {
                continue;
            }

            Assert.True(
                Tools.Contains(candidate),
                $"{relativePath} names '{candidate}' as if it were an MCP tool; the server does not expose it.");
        }
    }

    [Fact]
    public void No_document_offers_a_way_to_re_identify()
    {
        // The vault is the only artefact that can undo the work. There is no tool for this by design,
        // and a document that hints at one invites someone to build it.
        foreach (string file in (string[])["README.md", "SKILL.md", "skill/README.md", "src/Silueta.Mcp/README.md"])
        {
            foreach (string forbidden in (string[])["reidentify_", "re_identify_", "unredact", "reverse_redaction"])
            {
                Assert.DoesNotContain(forbidden, File.ReadAllText(Path.Combine(RepoRoot, file)), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// The tool whose whole reason to exist is that it takes a path. If its description ever stops
    /// leading with that, the agent reading it stops routing around its own context window.
    /// </summary>
    [Fact]
    public void The_inline_text_tool_still_warns_before_it_does_anything_else()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Silueta.Mcp", "Tools", "RedactionTools.cs"));
        int description = source.IndexOf("THE TEXT YOU PASS HERE IS ALREADY EXPOSED", StringComparison.Ordinal);
        int tool = source.IndexOf("Name = \"redact_text\"", StringComparison.Ordinal);

        Assert.True(tool >= 0, "redact_text is gone; this guard needs rewriting.");
        Assert.True(description > tool, "redact_text's description no longer opens with the exposure warning.");
    }

    /// <summary>Words in backticks that look like a tool name but are not one.</summary>
    private static readonly HashSet<string> KnownNonTools = new(StringComparer.Ordinal)
    {
        "subjectId", "recordId", "roster", "value", "kind", "caveat", "silueta_redact",
    };

    private static IReadOnlyList<string> ReadToolNames()
    {
        // The whole project, not just Tools/: a tool declared in a file somewhere else would be invisible
        // to a guard that only looks where tools are supposed to live, which is the one case the guard
        // exists for.
        string project = Path.Combine(RepoRoot, "src", "Silueta.Mcp");
        var attribute = new Regex(@"\[McpServerTool\((?<args>[^\]]*)\)", RegexOptions.Compiled);

        var found = new List<string>();
        foreach (string file in Directory
            .EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (Match match in attribute.Matches(File.ReadAllText(file)))
            {
                Match name = Regex.Match(match.Groups["args"].Value, @"Name\s*=\s*""(?<n>[^""]+)""");
                if (name.Success)
                {
                    found.Add(name.Groups["n"].Value);
                }
            }
        }

        Assert.NotEmpty(found);
        return found;
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

    [GeneratedRegex(@"`(?<name>[a-z][a-z0-9]*_[a-z0-9_]+)`")]
    private static partial Regex BacktickedIdentifier();
}
