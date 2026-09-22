# silueta — the skill that keeps the transcript out of the conversation

A drop-in **Claude Code / Codex / agent skill** for de-identifying conversation transcripts: shift
notes, support calls, clinical hand-offs — the text a speech recogniser produced, not the text someone
typed.

It is unusual among skills in that most of what it does is stop you. The ordinary agent reflex when
asked to redact a file is to open it, look, and decide what to remove. For a transcript of real people
that reflex *is* the leak: once the text is in the context window it is in the conversation history and
in whatever the provider logs, and no redaction afterwards reaches those copies. So the skill routes the
work through a path the model never opens, and reports counts instead of quoting content.

It is the front end of **[Silueta](https://github.com/peopleworks/Silueta)**, a dependency-free .NET
engine that matches a roster you already hold *through* the spelling damage a recogniser leaves behind —
`Sofía Reyes` arriving as `Sophia Rays`, `Eleanor Vasquez` as `Ellenor Vasques`.

## Install

The skill itself is [`SKILL.md`](../SKILL.md) in the repository root, which is where every installer
looks for it.

```bash
# one command, and it offers Claude Code, Codex, Gemini CLI, Cursor and the rest
npx skills add peopleworks/Silueta -g
```

As a Claude Code plugin, from the marketplace manifest in this repository:

```
/plugin marketplace add peopleworks/Silueta
/plugin install silueta
```

Or copy the one file yourself:

```bash
mkdir -p ~/.claude/skills/silueta && cp SKILL.md ~/.claude/skills/silueta/
```

Then use it:

```
/silueta de-identify shift-042.txt — the roster is in roster.json
```

## Pair it with the engine

The skill is judgment about flow; the engine does the work. Install the MCP server so the main tool
takes a **path** rather than text:

```jsonc
{ "mcpServers": { "silueta": { "command": "dnx", "args": ["Silueta.Mcp", "--prerelease", "--yes"] } } }
```

| Want | Tool | Identified text in the context? |
| --- | --- | --- |
| De-identify a transcript on disk | `redact_transcript` | **no** |
| De-identify text handed to you inline | `redact_text` | yes, already |
| Why did this name survive? | `explain_name_match` | no |
| What does a run catch without a roster? | `list_pattern_rules` | no |

There is no re-identification tool, and there will not be one.

## What makes it different

| | A generic "redact PII" skill | **silueta** |
| --- | --- | --- |
| Input | Reads the file into the context, then edits | **Passes a path**; the file is never read into the conversation |
| Built for | Text someone typed | **Text a recogniser produced**, where the name is misspelled |
| Names | Guesses which capitalised word is a person | Matches **a roster you already hold**, so it does not delete *Parkinson* |
| Re-identification | Often a helpful extra | **Refused** — the vault belongs to the agency |
| Honesty | "Done, your text is anonymised" | Gives the **measured leak rate with its interval and its corpus**, and names what survives |

## What it does not promise

Silueta's leak rate has been measured only on thirty synthetic transcripts, and it is high: most of them
still held something identifying. The skill is required to say so, with the interval and what the corpus
was, rather than paper over it — the numbers are in the repository README. Nicknames, people named only
by relationship, names nobody wrote down, and names damaged past the matcher's threshold all survive —
and places are found only in the forms the rules know: not a postal code nobody introduced as one, nor a
town named alone that the lineage does not list.

MIT. Pedro Hernández (PeopleWorks) · https://github.com/peopleworks/Silueta
