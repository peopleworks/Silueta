using System.Text.RegularExpressions;

namespace Silueta.Core.Tests;

/// <summary>
/// Holds the test project's list of .NET 10-only files to the one reason a file may be on it.
/// <para>
/// Silueta.Core ships for .NET 9 and .NET 10, and the promise that goes with that is that every test of
/// Core runs on both. The project leaves a few files out on .NET 9 because they use the CLI, the MCP server
/// or the calibration tool, which exist only on .NET 10. That list is exactly the kind of place a Core test
/// ends up when it fails on the older runtime and somebody is in a hurry — and then the library is being
/// shipped for a runtime it is no longer tested on, while the build stays green.
/// </para>
/// <para>
/// So every file on the list has to name one of those projects. A file that only uses Core has no business
/// there, and this fails by its name.
/// </para>
/// </summary>
public partial class NetTenOnlyTests
{
    private static readonly string Tests = Path.Combine(Repo.Root, "tests", "Silueta.Core.Tests");

    private static IReadOnlyList<string> Excluded()
    {
        string project = File.ReadAllText(Path.Combine(Tests, "Silueta.Core.Tests.csproj"));
        return [.. RemovedFile().Matches(project).Select(m => m.Groups["file"].Value)];
    }

    [Fact]
    public void The_list_is_where_this_guard_is_looking()
    {
        // Without this, a renamed element or a moved project turns every check below into a check of nothing.
        Assert.NotEmpty(Excluded());
    }

    [Fact]
    public void Every_file_left_out_on_net9_uses_a_program_that_only_exists_on_net10()
    {
        foreach (string file in Excluded())
        {
            string path = Path.Combine(Tests, file);
            Assert.True(File.Exists(path), $"The project leaves out {file} on .NET 9, and there is no such file.");

            Assert.True(
                NetTenOnlyProject().IsMatch(File.ReadAllText(path)),
                $"{file} is left out on .NET 9 but uses nothing from the CLI, the MCP server or the calibration " +
                "tool. A test of Core runs on both runtimes Core ships for — take it off the list.");
        }
    }

    [GeneratedRegex(@"<Compile\s+Remove=""(?<file>[^""]+)""")]
    private static partial Regex RemovedFile();

    [GeneratedRegex(@"\busing\s+Silueta\.(Cli|Mcp|Calibration)\b")]
    private static partial Regex NetTenOnlyProject();
}
