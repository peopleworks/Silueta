# Silueta.Mcp

The [Silueta](https://github.com/peopleworks/Silueta) de-identification engine as an MCP server:
four tools for Claude Desktop, Claude Code, VS Code, or any Model Context Protocol client.

```jsonc
// claude_desktop_config.json
{ "mcpServers": { "silueta": { "command": "dnx", "args": ["Silueta.Mcp", "--prerelease", "--yes"] } } }
```

```bash
dnx Silueta.Mcp --prerelease --yes                    # no install step
dotnet tool install --global Silueta.Mcp --prerelease # …or install `silueta-mcp` once
```

Every Silueta package is a pre-release for now, so every command asks for one: without `--prerelease` there
is no version to find. The engine is not finished and publishes a high leak rate, and the version number says
the same thing its README does. Needs .NET 10.

## Why the main tool takes a path and not text

The arguments of a tool call are written by the model. A tool shaped `redact(text)` requires the model
to have read the transcript in order to pass it — so by the time the redactor runs, the identified text
is already in the context window, in the conversation history, and in whatever the provider logs. The
tool can return clean text. It cannot un-expose its own input.

So `redact_transcript` takes a **path the model never opened**. This server reads the file, redacts it,
and returns the redacted text and a manifest of counts. The values that were removed do not come back,
and neither does the vault's mapping from a person to their invented name.

`redact_text` exists for text that is already safe to hold — a synthetic example, a corpus you
generated, a redacted file you are checking — and its description says so first, before anything else.

## The tools

| Tool | What it does | Touches PHI |
| --- | --- | --- |
| `redact_transcript` | De-identifies a file on disk against a roster; returns the redacted text and a manifest | reads the file, never returns the values |
| `redact_text` | The same for text passed inline — **already exposed by being passed** | yes, unavoidably |
| `explain_name_match` | Why the matcher does or does not treat two spellings as one name: keys, edit distance, ratio, threshold | no |
| `list_pattern_rules` | The pattern rules, and which Safe Harbor identifiers no rule emits | no |

Everything runs on the machine. Nothing is downloaded and nothing is uploaded.

**There is no re-identification tool, and there will not be one.** The vault is the only artefact that
can undo the work, every re-identification is meant to be logged by the person who did it, and a model
calling a tool is not that person.

## What the server refuses

`redact_transcript` takes a path the model wrote, which is the whole attack surface. So:

- every path is confined to one directory — `SILUETA_ROOT`, defaulting to where the server was started;
- a file that looks like a vault is refused outright, and a run whose vault is also its transcript or
  output is refused;
- the lineage — the word lists, labels and pattern rules a run uses — is `SILUETA_LINEAGE`, an
  environment variable and **not** a tool parameter. Which dictionaries a corpus is redacted with is a
  decision by whoever set this server up: a model that can choose the word lists can choose a lineage
  whose "labels" leave everything where it is, and the report would still say the run succeeded. The
  lineage's name and fingerprint come back in every report, so the model can say which one ran;
- the policy — what each kind becomes — is `SILUETA_POLICY`, one of the lineage's named policies, and Safe
  Harbor when unset. Also an environment variable and not a parameter, for a sharper reason: a model that
  could pick the policy could pick the one that keeps everything. A name the lineage does not define stops
  the run instead of falling back, and a policy that is not Safe Harbor says so, departure by departure, in
  the report's caveat;
- the redacted text is **withheld**, with the reason in a `withheld` field, when no roster was given
  (no name could be found and every name survived), when nothing was replaced, or when the run left
  residue. `outputPath` still writes a clean result to disk without it entering the context.

Without these, this tool is a general-purpose file reader with a reassuring name — and it was: pointing
it at the vault with no roster returned the whole subject-to-invented-name table, marked safe to export.

## Every result says whether it held

After redacting, the engine runs the same detectors over its own output. The report carries
`residualSpans` and `safeToExport`, and anything but zero means the text must not be passed on: an
invented name collided with someone real in that record, or a replacement joined the words around it to
spell one. Zero is not proof — residue is what this pipeline can see, so a name it never knew about is
missing from there too.

## What this does not promise

Silueta's leak rate has been measured only on a small synthetic corpus, and on it most transcripts still
held something identifying — the numbers and their limits are in the repository README. Nothing here is
verified to be de-identified. Names nobody wrote down — nicknames, a relative mentioned only by
relationship, a doctor named once — are invisible to the roster matcher and survive. A street address is found
only as the postal standards write it, a city only before a state or when the lineage lists it, a postal code
only after "ZIP", "código postal" or a state, and numbers spoken as words are not recognised. Every tool result says so in its own `caveat` field.

MIT. Pedro Hernández (PeopleWorks) · https://github.com/peopleworks/Silueta
