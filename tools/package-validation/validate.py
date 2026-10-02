"""Consume already-packed bits; never build or reference a repository project."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import shutil
import subprocess
import tempfile
import threading
import xml.etree.ElementTree as ET
from xml.sax.saxutils import quoteattr
import zipfile


ROOT = Path(__file__).resolve().parents[2]
IDS = ("Silueta.Core", "Silueta.Cli", "Silueta.Mcp")


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def inspect_packages(directory):
    packages, evidence, hashes = {}, {}, {}
    for identifier in IDS:
        paths = list(directory.glob(identifier + ".*.nupkg"))
        require(len(paths) == 1, f"Expected exactly one {identifier} nupkg, found {len(paths)}.")
        path = paths[0]
        with zipfile.ZipFile(path) as archive:
            metadata = ET.fromstring(archive.read(identifier + ".nuspec"))
            ns = {"n": metadata.tag.split("}")[0][1:]}
            get = lambda field: metadata.findtext("n:metadata/n:" + field, namespaces=ns)
            version = get("version")
            require(get("id") == identifier and version, f"Invalid {identifier} identity.")
            require(path.name == f"{identifier}.{version}.nupkg", "Package filename and identity disagree.")
            readme = get("readme")
            require(readme and archive.read(readme).strip(), f"{identifier} has no packaged README.")
            symbols = path.with_suffix(".snupkg")
            require(symbols.is_file(), f"{identifier} has no symbol package.")
            with zipfile.ZipFile(symbols) as symbol_archive:
                require(any(n.endswith(".pdb") for n in symbol_archive.namelist()), "Symbol package has no PDB.")
            if identifier == "Silueta.Core":
                for tfm in ("net9.0", "net10.0"):
                    hashes[tfm] = digest(archive.read(f"lib/{tfm}/Silueta.Core.dll"))
            else:
                settings = ET.fromstring(archive.read("tools/net10.0/any/DotnetToolSettings.xml"))
                command = settings.find("Commands/Command")
                expected = "silueta" if identifier == "Silueta.Cli" else "silueta-mcp"
                require(command is not None and command.attrib["Name"] == expected, "Tool command changed.")
                archive.read("tools/net10.0/any/" + command.attrib["EntryPoint"])
                require(digest(archive.read("tools/net10.0/any/Silueta.Core.dll")) == hashes["net10.0"],
                        f"{identifier} bundles a different Core assembly.")
            if identifier == "Silueta.Mcp":
                manifest = json.loads(archive.read(".mcp/server.json"))
                entries = [p for p in manifest["packages"] if p["registryType"] == "nuget"]
                require(manifest["version"] == version and len(entries) == 1
                        and entries[0]["identifier"] == identifier and entries[0]["version"] == version
                        and entries[0]["transport"]["type"] == "stdio", "Packaged MCP manifest disagrees with package.")
        packages[identifier] = (path, version)
        evidence[identifier] = {"version": version, "nupkgSha256": digest(path.read_bytes()),
                                "snupkgSha256": digest(symbols.read_bytes())}
    require(len({v for _, v in packages.values()}) == 1, "Package versions disagree.")
    return packages, evidence, hashes


def run(arguments, work, env, expected=0):
    result = subprocess.run([str(a) for a in arguments], cwd=work, env=env,
                            capture_output=True, text=True, encoding="utf-8", timeout=180)
    require(result.returncode == expected,
            f"{Path(str(arguments[0])).name} exited {result.returncode}, expected {expected}.\n"
            + result.stdout + result.stderr)
    return result.stdout


class Mcp:
    def __init__(self, command, work, env):
        self.process = subprocess.Popen([str(command)], cwd=work, env=env, stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                        text=True, encoding="utf-8", bufsize=1)
        self.messages = queue.Queue()
        self.next_id = 0
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.reader.start()

    def read(self):
        for line in self.process.stdout:
            self.messages.put(line)
        self.messages.put(None)

    def send(self, message):
        self.process.stdin.write(json.dumps({"jsonrpc": "2.0", **message}) + "\n")
        self.process.stdin.flush()

    def request(self, method, params):
        self.next_id += 1
        identifier = self.next_id
        self.send({"id": identifier, "method": method, "params": params})
        # Notifications may arrive first; a fixed deadline still bounds the complete request.
        import time
        deadline = time.monotonic() + 20
        while True:
            line = self.messages.get(timeout=max(0.001, deadline - time.monotonic()))
            require(line is not None, "MCP ended before answering.")
            message = json.loads(line)
            if message.get("id") == identifier:
                require("error" not in message, f"MCP protocol error: {message.get('error')}")
                return message["result"]
            require(time.monotonic() < deadline, "MCP response timed out.")

    def call(self, name, arguments, error=False):
        result = self.request("tools/call", {"name": name, "arguments": arguments})
        require(bool(result.get("isError")) == error, f"Unexpected MCP tool status for {name}.")
        return result

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=10)
        self.reader.join(timeout=5)
        self.process.stdout.close()


def report(result):
    data = result.get("structuredContent")
    if data is None:
        data = json.loads(next(c["text"] for c in result["content"] if c["type"] == "text"))
    require(any(k.lower() == "spansreplaced" for k in data), f"Unexpected MCP report fields: {list(data)}")
    return {k.lower(): v for k, v in data.items()}


def consume(packages, hashes, work):
    env = dict(os.environ)
    # No inherited operator choices, custom lineage or tool resolver cache.
    for key in list(env):
        if key.startswith("SILUETA_") or key.startswith("NUGET_"):
            del env[key]
    env.update({"NUGET_PACKAGES": str(work / "cache"), "NUGET_HTTP_CACHE_PATH": str(work / "http"),
                "NUGET_PLUGINS_CACHE_PATH": str(work / "plugins"), "DOTNET_CLI_HOME": str(work / "home"),
                "DOTNET_NOLOGO": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
                "PYTHONIOENCODING": "utf-8"})
    feed = work / "feed"
    feed.mkdir()
    for path, _ in packages.values():
        shutil.copyfile(path, feed / path.name)
    config = work / "NuGet.Config"
    config.write_text(f'''<configuration>
  <packageSources><clear/><add key="local" value={quoteattr(str(feed))}/>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/>
    <packageSource key="local"><package pattern="Silueta.*"/></packageSource>
    <packageSource key="nuget.org"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>''', encoding="utf-8")
    version = packages["Silueta.Core"][1]
    fixtures = work / "fixtures"
    shutil.copytree(ROOT / "tests/Silueta.Core.Tests/Fixtures/preview.3", fixtures)
    evidence = {"consumers": {}}
    for tfm in ("net9.0", "net10.0"):
        print(f"Restoring and running external Core consumer ({tfm})...", flush=True)
        project = work / tfm
        project.mkdir()
        (project / "Consumer.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>{tfm}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="Silueta.Core" Version="[{version}]"/></ItemGroup>
</Project>''', encoding="utf-8")
        shutil.copyfile(ROOT / "tools/package-validation/Consumer.cs.txt", project / "Program.cs")
        run(["dotnet", "restore", project / "Consumer.csproj", "--configfile", config, "--nologo"], work, env)
        # Prove restore used the exact local nupkg, not an older published package of the same version.
        cached = work / "cache/silueta.core" / version / f"silueta.core.{version}.nupkg"
        require(digest(cached.read_bytes()) == digest(packages["Silueta.Core"][0].read_bytes()), "Core restore used other bits.")
        output = run(["dotnet", "run", "--project", project, "-c", "Release", "--no-restore", "--", fixtures, project], work, env)
        result = json.loads(output.strip().splitlines()[-1])
        require(result["runtime"].startswith(".NET " + tfm[3:].split(".")[0] + "."),
                f"Consumer ran on the wrong runtime for {tfm}.")
        require(result["coreSha256"] == hashes[tfm], f"Consumer did not load packed Core for {tfm}.")
        evidence["consumers"][tfm] = result
    tools = work / "installed-tools"
    for identifier in IDS[1:]:
        print(f"Installing {identifier} from local feed...", flush=True)
        run(["dotnet", "tool", "install", identifier, "--version", version,
             "--tool-path", tools, "--configfile", config], work, env)
        installed_packages = list((tools / ".store" / identifier.lower()).rglob(f"{identifier.lower()}.{version}.nupkg"))
        require(len(installed_packages) == 1
                and digest(installed_packages[0].read_bytes()) == digest(packages[identifier][0].read_bytes()),
                f"Installed {identifier} used other package bits (archives: {installed_packages}).")
        installed = list((tools / ".store" / identifier.lower()).rglob("Silueta.Core.dll"))
        require(len(installed) == 1 and digest(installed[0].read_bytes()) == hashes["net10.0"],
                f"Installed {identifier} loaded different Core bits.")
    suffix = ".exe" if os.name == "nt" else ""
    cli = tools / ("silueta" + suffix)
    demo = run([cli, "demo"], work, env)
    redacted = demo.split("--- de-identified ---")[1].split("--- what was replaced ---")[0]
    for leak in ("Ellenor", "Vasques", "Jamileth", "Sophia", "602-555-0147", "jamileth.v@example.com", "94 years old"):
        require(leak not in redacted, "Installed CLI demo leaked a fixture identifier.")
    block = re.search(r"<!-- demo-output:start[^>]*-->(.*?)<!-- demo-output:end -->",
                      (ROOT / "README.md").read_text(encoding="utf-8"), re.DOTALL)
    require(block is not None and block[1].strip(), "README demo block is missing.")
    claimed = block[1]
    for line in claimed.splitlines():
        if line.strip() and not line.startswith("```"):
            require(line in redacted, "Installed CLI demo disagrees with README.")
    roster = work / "people.json"
    roster.write_text(json.dumps([{"value": "Sofia Reyes", "kind": "PatientName", "subjectId": "s-1"}]), encoding="utf-8")
    cli_vault = work / "cli-vault.json"
    shutil.copyfile(fixtures / "vault-v2.json", cli_vault)
    output, manifest = work / "cli-output.txt", work / "cli-manifest.json"
    arguments = [cli, "redact", "--in", fixtures / "input.txt", "--record", "package-cli",
                 "--context", roster, "--lineage", fixtures / "lineage.json", "--vault", cli_vault,
                 "--out", output, "--manifest", manifest]
    run(arguments, work, env)
    expected = (fixtures / "output.txt").read_text(encoding="utf-8").rstrip("\r\n")
    require(output.read_text(encoding="utf-8").rstrip("\r\n") == expected, "Installed CLI redaction changed.")
    require(json.loads(manifest.read_text(encoding="utf-8"))["outputSha256"] == digest(output.read_bytes()), "CLI manifest does not bind output.")
    saved = [p.read_bytes() for p in (cli_vault, output, manifest)]
    run(arguments + ["--max-input-chars", "2"], work, env, expected=4)
    require(saved == [p.read_bytes() for p in (cli_vault, output, manifest)], "CLI limit failure changed artifacts.")
    run(arguments, work, env)
    require(cli_vault.read_bytes() == saved[0] and output.read_bytes() == saved[1], "CLI reuse changed assignments.")
    evidence["cli"] = ["installed-shim", "demo-canary", "readme-demo", "historical-redaction", "manifest-hash", "limit-no-writes", "vault-reuse"]
    print("Checking installed MCP over stdio...", flush=True)
    env.update({"SILUETA_ROOT": str(work), "SILUETA_LINEAGE": str(fixtures / "lineage.json"),
                "SILUETA_MAX_INPUT_CHARACTERS": "128"})
    mcp_vault, mcp_output = work / "mcp-vault.json", work / "mcp-output.txt"
    shutil.copyfile(fixtures / "vault-v2.json", mcp_vault)
    client = Mcp(tools / ("silueta-mcp" + suffix), work, env)
    try:
        client.request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                      "clientInfo": {"name": "package-validation", "version": "1"}})
        client.send({"method": "notifications/initialized"})
        declarations = client.request("tools/list", {})["tools"]
        required = {"redact_transcript": {"transcriptPath", "recordId"}, "redact_text": {"text", "recordId"},
                    "explain_name_match": {"a", "b"}, "list_pattern_rules": set()}
        require(set(required) <= {t["name"] for t in declarations}, "Installed MCP tool names changed.")
        for tool in declarations:
            if tool["name"] not in required:
                continue  # Additional tools do not break existing callers.
            require(set(tool["inputSchema"].get("required", [])) == required[tool["name"]], "MCP required arguments changed.")
            require("cancellationToken" not in tool["inputSchema"]["properties"], "Cancellation token leaked into schema.")
        call_args = {"transcriptPath": str(fixtures / "input.txt"), "recordId": "package-mcp",
                     "rosterPath": str(roster), "vaultPath": str(mcp_vault), "outputPath": str(mcp_output)}
        first = report(client.call("redact_transcript", call_args))
        require(first.get("redactedtext") is None and first["residualspans"] == 0 and first["spansreplaced"] == 3,
                "MCP output-path contract changed.")
        require(mcp_output.read_text(encoding="utf-8").rstrip("\r\n") == expected, "Installed MCP redaction changed.")
        saved = [p.read_bytes() for p in (mcp_vault, mcp_output)]
        client.call("redact_transcript", call_args)
        require(saved == [p.read_bytes() for p in (mcp_vault, mcp_output)], "MCP reuse changed assignments.")
        oversized = work / "oversized.txt"
        oversized.write_text("Sofia Reyes " * 20, encoding="utf-8")
        failure = client.call("redact_transcript", {**call_args, "transcriptPath": str(oversized)}, error=True)
        require("Sofia Reyes" not in json.dumps(failure) and saved == [p.read_bytes() for p in (mcp_vault, mcp_output)],
                "MCP limit failure disclosed input or changed artifacts.")
        inline = report(client.call("redact_text", {"text": "Call 555-0147.", "recordId": "package-inline"}))
        require(inline["spansreplaced"] == 1 and "555-0147" not in inline["redactedtext"], "MCP inline redaction failed.")
        client.call("explain_name_match", {"a": "Sofia", "b": "Sofia"})
        client.call("list_pattern_rules", {})
        evidence["mcp"] = ["installed-shim", "initialize", "four-tool-schemas", "historical-redaction",
                           "output-withheld", "vault-reuse", "limit-no-writes", "redact-text", "explain", "patterns"]
    finally:
        client.close()
    return evidence


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", type=Path, help="Directory containing exactly three nupkg/snupkg pairs")
    parser.add_argument("--report", type=Path, required=True, help="Write evidence only after every check passes")
    arguments = parser.parse_args()
    require(arguments.report.suffix == ".json", "The validation report must be a .json file.")
    arguments.report.unlink(missing_ok=True)
    packages, evidence, hashes = inspect_packages(arguments.packages.resolve())
    # TemporaryDirectory owns only this new directory; never clear the user's NuGet caches or tools.
    with tempfile.TemporaryDirectory(prefix="silueta-packages-") as directory:
        installed = consume(packages, hashes, Path(directory).resolve())
    arguments.report.parent.mkdir(parents=True, exist_ok=True)
    arguments.report.write_text(json.dumps({"packages": evidence, "coreAssemblySha256": hashes,
                                           **installed}, indent=2) + "\n", encoding="utf-8")
    print("Package validation passed: Core net9/net10, installed CLI and all four MCP tools.", flush=True)


if __name__ == "__main__":
    main()
