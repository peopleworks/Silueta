# Silueta.Mcp

The [Silueta](https://github.com/peopleworks/Silueta) de-identification engine as an MCP server:
four tools for Claude Desktop, Claude Code, VS Code, or any Model Context Protocol client.

```jsonc
// claude_desktop_config.json
{ "mcpServers": { "silueta": { "command": "dnx", "args": ["Silueta.Mcp", "--yes"] } } }
```

```bash
dnx Silueta.Mcp --yes                    # no install step
dotnet tool install --global Silueta.Mcp # …or install `silueta-mcp` once
```

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

## Every result says whether it held

After redacting, the engine runs the same detectors over its own output. The report carries
`residualSpans` and `safeToExport`, and anything but zero means the text must not be passed on: an
invented name collided with someone real in that record, or a replacement joined the words around it to
spell one. Zero is not proof — residue is what this pipeline can see, so a name it never knew about is
missing from there too.

## What this does not promise

Silueta has not yet measured its own leak rate, so nothing here is verified to be de-identified. Names
nobody wrote down — nicknames, a relative mentioned only by relationship, a doctor named once — are
invisible to the roster matcher and survive. No rule emits a postal code or a street address yet, and
numbers spoken as words are not recognised. Every tool result says so in its own `caveat` field.

MIT. Pedro Hernández (PeopleWorks) · https://github.com/peopleworks/Silueta
