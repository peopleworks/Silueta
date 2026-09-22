using System.Text.Json;
using System.Text.RegularExpressions;

namespace Silueta.Core.Tests;

/// <summary>
/// The version a package is published under, and the two other places that have to agree with it.
/// <para>
/// The MCP server carries a <c>server.json</c> inside its own package, naming the package id and the version a
/// client should fetch. It said <c>0.1.0</c> the day the csproj files moved to <c>0.1.0-preview.1</c>, which
/// would have published a server whose own manifest points at a version that does not exist. Found while
/// reading before the first publish; this is what stops it happening at the second.
/// </para>
/// <para>
/// And while the packages are a pre-release, an install command without <c>--prerelease</c> finds nothing:
/// <c>dotnet tool install</c> and <c>dnx</c> look for a stable version, and there is none. The READMEs ship
/// inside the packages and are frozen on nuget.org at publish time, so an instruction that is wrong on that
/// day stays wrong on the package page.
/// </para>
/// </summary>
public partial class PackageVersionTests
{
    private static string VersionOf(string project)
    {
        string csproj = File.ReadAllText(Path.Combine(Repo.Root, project));
        Match match = VersionElement().Match(csproj);
        Assert.True(match.Success, $"{project} has no <Version>.");
        return match.Groups["v"].Value;
    }

    [Fact]
    public void The_three_packages_are_one_version()
    {
        // nuget.yml refuses a release otherwise; this says so before anybody tags one.
        string core = VersionOf("src/Silueta.Core/Silueta.Core.csproj");

        Assert.Equal(core, VersionOf("src/Silueta.Cli/Silueta.Cli.csproj"));
        Assert.Equal(core, VersionOf("src/Silueta.Mcp/Silueta.Mcp.csproj"));
    }

    [Fact]
    public void The_MCP_server_manifest_names_the_version_it_ships_in()
    {
        string package = VersionOf("src/Silueta.Mcp/Silueta.Mcp.csproj");

        using JsonDocument server = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Repo.Root, "src", "Silueta.Mcp", ".mcp", "server.json")));

        Assert.Equal(package, server.RootElement.GetProperty("version").GetString());

        JsonElement nuget = Assert.Single(
            server.RootElement.GetProperty("packages").EnumerateArray(),
            p => p.GetProperty("identifier").GetString() == "Silueta.Mcp");
        Assert.Equal(package, nuget.GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SKILL.md")]
    [InlineData("skill/README.md")]
    [InlineData("src/Silueta.Mcp/README.md")]
    public void While_the_packages_are_a_prerelease_no_install_command_leaves_out_the_flag(string document)
    {
        if (!VersionOf("src/Silueta.Core/Silueta.Core.csproj").Contains('-', StringComparison.Ordinal))
        {
            return; // a stable version installs without the flag, and keeping it is harmless
        }

        string[] lines = File.ReadAllLines(Path.Combine(Repo.Root, document));
        List<string> installs = [.. lines.Where(l => InstallCommand().IsMatch(l))];

        foreach (string line in installs)
        {
            Assert.True(
                line.Contains("--prerelease", StringComparison.Ordinal),
                $"{document} tells people to run \"{line.Trim()}\", which finds nothing while every Silueta " +
                "package is a pre-release. Add --prerelease.");
        }
    }

    [GeneratedRegex(@"<Version>(?<v>[^<]+)</Version>")]
    private static partial Regex VersionElement();

    // Something that fetches a Silueta package: a tool install, a package reference, or dnx — in a shell line
    // or in the argument list of an MCP client's configuration.
    [GeneratedRegex(@"dotnet\s+tool\s+install\b.*\bSilueta\.|dotnet\s+add\s+package\s+Silueta\.|\bdnx\b[^\n]*Silueta\.Mcp|""dnx""")]
    private static partial Regex InstallCommand();
}
