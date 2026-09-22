namespace Silueta.Core.Tests;

/// <summary>
/// Where the repository is, for the guards that read its documents and its corpus.
/// <para>
/// This lived on <c>McpToolDocumentationTests</c>, the first guard that needed it, and every test that
/// read a file borrowed it from there. That was harmless while the whole project targeted one framework.
/// It stopped being harmless when Core started targeting .NET 9 as well: the tests of the library itself
/// were reaching for the path through a class about the MCP server, and would have had to follow it into
/// whichever framework that server builds for.
/// </para>
/// </summary>
internal static class Repo
{
    /// <summary>The directory holding <c>Silueta.slnx</c>, found by walking up from the test binary.</summary>
    public static readonly string Root = Find();

    private static string Find()
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
