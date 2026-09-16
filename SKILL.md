---
name: silueta
description: >-
  De-identify conversation transcripts — the text a speech recogniser produced — without pulling the
  identified version into the conversation first. Use when someone wants to redact, anonymise or
  de-identify a transcript, shift note, call recording or meeting log; when they are about to paste
  clinical or customer text into a chat; when they ask what HIPAA Safe Harbor requires of a transcript;
  or when they mention Silueta. Also use to explain why a name survived a redaction. Backed by the
  Silueta engine (MCP server, CLI or .NET library), which matches a roster you already hold through ASR
  spelling damage. It has not yet measured its own leak rate and must never be described as proof that
  a transcript is de-identified.
---

# Silueta — de-identify a transcript without reading it first

Your job here is mostly to **not** do something. A transcript of a home visit, a support call or a
clinical hand-off identifies real people, and the ordinary agent reflex — open the file, look at it,
decide what to remove — is the one move that cannot be undone.

## The rule that comes before every other rule

**Reading an identified transcript into the conversation is the leak.** Not a step towards a leak: the
leak. Once the text is in the context window it is in the conversation history and in whatever the
provider logs, and no redaction afterwards reaches any of those copies.

So:

- **Never `cat`, `Read`, `head`, `grep` or open a transcript you have been asked to de-identify.** Pass
  its path to the tool and let the tool read it.
- **Never paste transcript content into your reply**, not even one line, not even to show what was
  found. The manifest says how many spans of each kind were replaced; that is the report.
- **If the user already pasted the text**, say so plainly and once: it is already in this conversation
  and redacting it now does not remove it from here. Then redact it anyway — a clean copy is still worth
  having — and suggest that the next one go through a path.
- **Never re-identify.** The vault maps an invented name back to a real person. It stays with the
  agency, every use of it is meant to be logged by the person who did it, and you are not that person.
  There is no tool for this, and asking for one is the sign that something has gone wrong upstream.

## How to run it

The MCP server is the best hand-off, because the result comes back structured and the main tool takes a
path:

```jsonc
// claude_desktop_config.json — or any MCP client
{ "mcpServers": { "silueta": { "command": "dnx", "args": ["Silueta.Mcp", "--yes"] } } }
```

| Want | Tool | Identified text in your context? |
| --- | --- | --- |
| De-identify a transcript on disk | `redact_transcript` | **no** — this is the one to use |
| De-identify text you were handed inline | `redact_text` | yes, already, unavoidably |
| Why did this name survive? | `explain_name_match` | no |
| What does a run catch without a roster? | `list_pattern_rules` | no |

The server only touches one directory: set `SILUETA_ROOT` to the folder holding the corpus, or start it
there. The word lists and the labels it replaces with are the **lineage**, set by whoever started the
server (`SILUETA_LINEAGE`) and never by you. Every report names it and carries its fingerprint: when you
say what ran, say which lineage ran, because two corpora redacted under different word lists are not the
same corpus. It refuses to read a vault, and it **withholds the redacted text** — returning a `withheld` reason
instead — when no roster was given, when nothing was replaced, or when the run left residue. Read the
reason out to the user; do not go looking for another route to the same text, because every other route
puts it in your context.

Without an MCP client, the command line does the same work and is just as safe, because the path is
still the argument:

```bash
dotnet tool install --global Silueta.Cli

silueta redact --in shift-042.txt --context roster.json --record r-042 \
               --out shift-042.deid.txt --manifest shift-042.manifest.json --vault vault.json
```

Pass `--vault` on every transcript of one corpus, or each run invents different names for the same
people. Pass `--out` when even the redacted text should stay out of the conversation.

## The roster is the job

Silueta finds **the people the caller already knows** — the patient, the family, the staff on shift —
through whatever the recogniser did to their names. Without a roster only the shape rules fire (phone,
e-mail, URL, IP, record numbers, dates, ages over 89) and **every name in the transcript survives**.

```json
[
  { "value": "Eleanor Vasquez", "kind": "PatientName", "subjectId": "patient-1" },
  { "value": "Yamilet Vasquez", "kind": "FamilyName",  "subjectId": "family-1" },
  { "value": "Sofía Reyes",     "kind": "StaffName",   "subjectId": "staff-1"  }
]
```

Help the user build this, and insist on two things:

- **`subjectId` must be opaque** — `patient-1`, `s-7f3` — never the person's name. It is the key the
  vault is filed under; a vault keyed by names is a roster.
- **`--record` must be opaque too**, and must not be the file name. It goes in the manifest, and the
  manifest travels with the corpus.

Spell names the way the *agency* writes them, not the way the transcript does. That is the whole point:
the transcript says `Sophia Rays`, the roster says `Sofía Reyes`, and Silueta is what connects them.

## What you may and may not say about the result

1. **Check `residualSpans` before you say anything.** Every run reads its own output back with the same
   detectors. If the result reports `safeToExport: false`, say so plainly, do not pass the text on, and
   tell the user which record it was — an invented name collided with someone real in that record, or a
   replacement joined the words around it to spell one. Zero residue is not proof of anything either:
   it is what this pipeline can see, so a name it never knew is missing from it too.
2. **Never call a transcript "de-identified" flatly.** Say what ran and what it found: "the roster
   matcher and the pattern pack ran; nine spans were replaced across three subjects." Whether a corpus
   may leave a building is a lawyer's decision, and an expert determination is a person signing a name.
3. **Silueta has not yet measured its own leak rate.** The library is built around that number and the
   number does not exist. If you are asked how good it is, say that, and do not substitute an
   impression. Every tool result carries the same caveat in a `caveat` field — pass it on.
4. **Name what survives, because it is predictable.** Nicknames (`Ellie` for Eleanor). Names nobody
   wrote down — a neighbour, a doctor mentioned once. People referred to only by relationship ("my
   daughter"). Names the recogniser damaged past the matcher's threshold: `Reyes` heard as `Rays`
   scores 0.40 against a threshold of 0.84, and that is a matcher failure, not a design decision.
5. **No rule emits a postal code or a street address yet**, and numbers or dates spoken as words
   ("five five five, oh one four seven", "September eleventh") are not recognised at all. If the
   transcript has those, say they were not touched.
6. **Companies and products come back as labels unless the lineage brings names for them.** Use
   `Organization` for a company and `Product` for a product; `ClientName` is a **person** — in home care
   the client is the patient — and a client that is a company is an `Organization`. The lineage that
   ships has no company names on purpose, since an invented company is very likely a real one, so a
   company becomes `[ORGANIZATION]` and the report's manifest lists it under `surrogatesUnavailable`.
   Say that plainly rather than calling it replaced by an invented name. And a company is matched only
   in the words the roster gave it: `Acme Corporation` on the roster does not find `Acme Corp` or `Acme`
   alone. If the transcript uses short forms, say they were not touched unless each form was on the
   roster under the same `subjectId`.
7. **A clean-looking output is not evidence.** The honest close is what ran, what it replaced, and what
   it is known not to catch.

## When a name survives and someone asks why

Run `explain_name_match` with the two spellings. It gives the phonetic key of each, the edit distance,
the similarity ratio and the threshold — which turns "it missed it" into "`reyes` and `rais` are three
edits apart, 0.40 against 0.84." That is a sentence someone can act on: add the alias to the roster, or
lower the threshold and accept more false positives.

## The test that no tool can run

The **motivated intruder test**: someone who knows the people reads the redacted transcripts and tries
to name them. For a small agency in one city, that decides whether a corpus is really de-identified,
and no recall number substitutes for it. Recommend it when the output is going anywhere that matters.

## Source and license

Silueta by Pedro Hernández (PeopleWorks) — de-identification for conversation transcripts, built for
text a speech recogniser produced. Repo: https://github.com/peopleworks/Silueta · MIT.
`Silueta.Core` has no dependencies and runs offline, inside the environment allowed to hold the
identified text. How the algorithm works, and what each choice costs, is in `Docs/ALGORITHM.md`.
