# Installed package validation

Pack the three products, then validate the artifacts a consumer will actually install:

```powershell
dotnet pack src/Silueta.Core -c Release -o artifacts/packages
dotnet pack src/Silueta.Cli -c Release -o artifacts/packages
dotnet pack src/Silueta.Mcp -c Release -o artifacts/packages
python tools/package-validation/validate.py artifacts/packages --report artifacts/packages/validation-report.json
```

Requires Python 3.9+, the .NET 10 SDK, the .NET 9 and 10 runtimes, and access to nuget.org for external
dependencies/framework reference packs. On Linux use `python3`. No Python packages are required.
Use a directory with exactly one version of each package and its adjacent symbol package; old
versions in that directory fail validation instead of being selected accidentally.

The probe never repacks or references a repository project. It copies the three nupkg files into its
own temporary feed, creates external consumer projects, and sets separate NuGet package, HTTP,
plugin and CLI-home directories. Both tools install into a private tool path. It leaves the user's
installed tools and caches alone and deletes only the temporary directory it created.

NuGet source mapping assigns `Silueta.*` exclusively to the temporary feed; external dependencies
use nuget.org. This matters while local and published previews have the same version. The probe also
compares restored/installed nupkg hashes and loaded Core assembly hashes with the input packages.
See [NuGet source mapping and its cache limitations](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping).

Checks cover:

- Package identity/version, both Core target assemblies, packaged README files and symbol PDBs.
- CLI/MCP command entry points, their bundled Core assembly and the packaged MCP server manifest.
- Core consumption on each runtime: preview.3 files and output, manifest hashes, retained names,
  stale-save rejection, limits, cancellation, embedded lineage, measured leak rate and pattern rules.
- Installed CLI: demo canary/README, historical redaction, output hash, vault reuse and limit failures
  that preserve existing vault, transcript and manifest files.
- Installed MCP over stdio: initialization, four tool names and required arguments, historical
  redaction, output withholding, vault reuse, limit failures with no writes/input disclosure, and
  calls to every tool. Protocol responses use deadlines rather than startup sleeps.

The JSON report records hashes for every input nupkg/snupkg and the checks/runtimes that passed.
An earlier report at the specified `.json` path is removed before validation; a failing run leaves
no success report. CI uploads the report; the release build includes it alongside the **same**
packages that await approval and get pushed. Neither workflow publishes a package from this probe.

These are synthetic installation and contract checks. Passing them does not establish detection
accuracy on a new corpus or replace an independent evaluation before selecting a stable-use scope.
