<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="Docs/brand/silueta-dark.svg">
  <img src="Docs/brand/silueta.svg" alt="Silueta" width="96" height="96">
</picture>

<!-- The mark is a sheet of canvas with the S cut out of it: a silhouette is what is left when the person
     is taken away, so the letter is the hole rather than the drawing. Docs/brand/. -->

# Silueta

**Give an AI your conversations — not the people in them.**

De-identification for the text an organisation wants analysed: calls, home visits, shift notes, support
chats. Built hardest for what a speech recogniser writes, where every name arrives misspelled.

[![CI](https://github.com/peopleworks/Silueta/actions/workflows/ci.yml/badge.svg)](https://github.com/peopleworks/Silueta/actions/workflows/ci.yml)
[![CodeQL](https://github.com/peopleworks/Silueta/actions/workflows/codeql.yml/badge.svg)](https://github.com/peopleworks/Silueta/actions/workflows/codeql.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Core: zero dependencies](https://img.shields.io/badge/core-zero%20dependencies-39454E?style=flat-square)](src/Silueta.Core)
[![MCP server](https://img.shields.io/badge/MCP-server-4E7C6B?style=flat-square)](#use-it-from-an-agent)
[![Agent skill](https://img.shields.io/badge/agent-skill-4E7C6B?style=flat-square)](SKILL.md)
[![Leak rate: measured, and high](https://img.shields.io/badge/leak%20rate-measured%2C%20and%20high-C0503F?style=flat-square)](#the-number)
[![Try it in your browser](https://img.shields.io/badge/try%20it-in%20your%20browser-4E7C6B?style=flat-square)](https://peopleworks.github.io/Silueta/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![PeopleWorks](https://img.shields.io/badge/by-PeopleWorks-636f61?style=flat-square)](https://mvp.microsoft.com/en-US/mvp/profile/24060a02-dbc6-44ec-bca5-c213ff9835c5)

<img src="Docs/brand/hero.svg" width="900"
     alt="Two panels. On the left, a shift report as a speech recogniser wrote it, with every name misspelled compared with the agency's roster of Sofía Reyes, Eleanor Vasquez and Yamilet Vasquez. An arrow labelled matched by sound, policy safe harbor, leads to the right panel: the same report as Silueta hands it on, where people became other invented people and the phone number and date became a label and a year. The surname Rays is still there and is marked as a failure the leak rate counts. Along the bottom: the manifest travels, the vault never does, and the failure rate is measured.">

<sub>The example is the demo below — including the name that is still in it, which is counted as a failure
and published with the rest.</sub>

</div>

---

A silhouette keeps the shape and loses the face. What was said, felt and decided survives; who said it
does not. *Patient 1 has high cholesterol* carries exactly the statistical content of the same sentence
with a real name in it, and none of the identity — so the sentiment analysis, the cohort, the dashboard
and the summary all still work on text that no longer says whose life it describes.

Silueta is for the moment an organisation wants to put its own text in front of an AI it does not run,
and the reason to hesitate is not the analysis — it is that the text says who. Typed text is the easy
case. The case it is built for is **the text a speech recogniser produced**, and it is built to **measure
how often it fails** rather than to promise that it doesn't. Both are narrower claims than "PII
redaction", and they are the two this repository can defend.

### Why it exists

> Removing a name is easy. Removing it after a recogniser misspelled it, keeping the text worth
> analysing, and publishing how often you still failed — is not.

| Concern | How Silueta handles it |
|---|---|
| 🧬 **The analysis needs the shape, not the face** | People become invented people of the same kind; phone numbers and e-mail addresses become labels. Sentiment, cohorts and timelines still read. |
| 🎙️ **A recogniser misspells every name** | The roster you already hold is matched by *sound*, not by letters — `Ellenor Vasques` is found as `Eleanor Vasquez`. |
| 🔁 **One person has to stay one person** | A vault gives each subject one invented name across the whole corpus — and the vault never leaves the building. |
| 🤖 **The redactor must not be the leak** | The MCP server takes a *path* the model never opens, so the identified text never enters a context window. |
| 📏 **You need to know how often it fails** | A leak rate measured on a published corpus, with its interval, and checked against the code on every build. |
| 🗂️ **Your data, your words** | Word lists, labels and pattern rules come from a *lineage* file you write, in your own language. |

---

## See it

```
Shift report. Sophia Rays was with Mrs. Ellenor Vasques this morning.
Her daughter Jamileth called at 602-555-0147 about the 3/14/2026 appointment,
and Ellie said she would email jamileth.v@example.com. The patient is 94 years old.
Blood pressure 138 over 82, pain 4 out of 10, and she took the warfarin with breakfast.
```

<!-- demo-output:start — CI checks this block against `silueta demo`; see .github/workflows/ci.yml -->
```
Shift report. Yael Rays was with Mrs. Ale Espinal this morning.
Her daughter Chris called at [PHONE] about the 2026 appointment,
and Ellie said she would email [EMAIL]. The patient is 90 or older.
Blood pressure 138 over 82, pain 4 out of 10, and she took the warfarin with breakfast.
```
<!-- demo-output:end -->

Not one of those names was spelled the way the agency spells it. `Sofía Reyes` arrived as `Sophia Rays`,
`Eleanor Vasquez` as `Ellenor Vasques`, `Yamilet` as `Jamileth`. A redactor that looks for names as they
are written on the roster misses every one, and a missed name is a leak.

**And two names are still in that output, which is the more useful half of the example.**

`Rays` survived, and `Reyes` is on the roster four lines above — this is not a name nobody knew, it is a
name the matcher failed on. Their phonetic keys are `reyes` and `rais`: three edits apart, a similarity
of 0.40 against a threshold of 0.84. `Ellie` survived for a different reason: it is a nickname, `eleanor`
against `elie` scores 0.29, and nothing in the roster or the rules knows that Eleanors are called Ellie.
One is a matcher that needs work; the other is a category the library does not handle yet. Both are
counted as failures, neither is a design decision, and the difference between them is exactly what a
leak rate is for. That rate is [measured, and it is below](#the-number).

## Try it

**[In your browser, with nothing uploaded →](https://peopleworks.github.io/Silueta/)** Paste a transcript,
name the people it is about, and watch what happens to it. The page prints what was found and how, what
survived, and this build's own measured failure rate beside the result. It is a Blazor WebAssembly page
with no server behind it and no HTTP client in it: the library runs in the tab, so the transcript has
nowhere to go, and the network tab is the proof. Use invented text anyway — it is a public page and
somebody may be looking over your shoulder.

```bash
dotnet run --project src/Silueta.Cli -- demo                  # the example above
silueta evaluate --gold corpus-synthetic/tts-asr              # the published number, reproduced
```

```bash
silueta redact --in shift-042.txt --context roster.json --record r-042 \
               --out shift-042.deid.txt --manifest shift-042.manifest.json --vault vault.json
```

`roster.json` is who this record is about:

```json
[
  { "value": "Eleanor Vasquez", "kind": "PatientName", "subjectId": "patient-1" },
  { "value": "Yamilet Vasquez", "kind": "FamilyName",  "subjectId": "family-1" },
  { "value": "Sofía Reyes",     "kind": "StaffName",   "subjectId": "staff-1"  }
]
```

Both ids are required and both must be opaque. `--record` goes into the manifest and `subjectId` is the
key the vault is filed under, so a record named after its file (`Ana-Perez.txt`) or a subject keyed by
the patient's name would publish the identifier through the very files that exist to show none were
published. Pass `--vault` on every run of a corpus: it is where the invented names live, and without it
each document invents new ones for the same people.

`kind` is required too, and has to be one of the names listed by `silueta --help`. A kind that cannot be
read stops the run and names the entry by position — never by its value — with the closest real kind
(`Organisation` gets "the closest kind is Organization"). It used to be read as `OtherName` without a
word, which sends a misspelled company to the pool of people's names.

## Invented names, not blanks

Look at the output above: the people became *other people*, while the phone number and the e-mail became
`[PHONE]` and `[EMAIL]`. That split is deliberate, and it is the difference between text an AI can
analyse and text it can only count.

**Names become invented names.** Replace them with markers and the text stops being text: the grammar
breaks, and a model reading it loses track of who "she" and "her daughter" refer to — which is exactly
what sentiment, behaviour and timeline analysis are built on. A realistic surrogate keeps the sentence
readable and keeps the reference chain intact, and it costs nothing, because a name carries no analytic
signal in the first place. The invented names are gender-neutral on purpose: guessing a real person's
gender is an inference this library has no business making.

**Shapes become masks.** A phone number, an e-mail or a record number carries no signal worth preserving
either, and an invented one is somebody's real number. So those are removed rather than replaced.

**And the vault is what makes a dashboard possible.** For statistics you need Patient 1 to be the same
Patient 1 across five hundred documents — that is what cohorts, trends and "this patient is
deteriorating" are made of. The vault is what keeps that stable, and it is also why it never leaves the
building: a stable pseudonym is exactly what links documents together, for you and for anyone else who
gets hold of it.

Those invented names, and the labels around them, come from a **lineage** — see below. Nothing about the
words is compiled in.

## Bring your own dictionaries: the lineage

A clinic, a call centre and a law firm do not redact the same things, do not speak the same language, and
have no reason to share one vendor's word lists. A **lineage** is a file you write that says what Silueta
replaces things with: the pools an invented name is drawn from, the text that stands in for each kind of
identifier, and the pattern rules to run.

```jsonc
{
  "lineage": "clinica-navi",         // identity; it goes in the manifest
  "version": "2",
  "language": "es-MX",               // recorded, NOT used to pick phonetic rules — see below
  "pools": {
    "given":   ["Ale", "Noa", "María José"],
    "family":  ["Bravo", "Toledo", "De la Cruz"],
    "company": ["Aurora Servicios", "Meridiano Logística"],   // optional; see below
    "product": ["Nimbo", "Serie 7"]                             // optional; digits allowed here
  },
  "labels": { "Phone": "[TELÉFONO]", "Email": "[CORREO]" },
  "generalizations": { "AgeOver89": "90 o más" },
  "patterns": [ { "id": "expediente", "kind": "RecordNumber", "regex": "EXP-\\d{6}", "confidence": 0.95 } ]
}
```

```bash
silueta redact --in visita.txt --record r-042 --lineage clinica-navi.json ...
```

For the MCP server it is the environment variable `SILUETA_LINEAGE`, deliberately and not a tool
parameter: which dictionaries a corpus is redacted with is a decision by whoever set the server up, and a
model that can choose the word lists can choose a lineage whose "labels" leave everything where it is.

The lineage's **fingerprint goes into every manifest**, digested from its content rather than its version
number, so an edit to a word list that forgets to bump the version still produces a corpus you can tell
apart from the one before it. Ship no lineage and you get the one embedded in the build — the same lists
this library always had, which now live in [a JSON file](src/Silueta.Core/Lineage/Lineages/lineage.core.json)
rather than in the source.

What is worth knowing before you write one:

- **A pool is checked when it loads, not when a corpus goes wrong.** Entries with digits are refused (a
  name with a number in it reads as a record number and gets found again by the pattern rules) — except in
  `product` pools, where "Serie 7" is a real shape and the vault still never emits a name the rules would
  find. So are duplicates once accents and case are folded, and so are two entries that *sound alike to
  this matcher*: two surrogates it cannot tell apart merge two people the next time the corpus is read.
- **Company and product pools are yours to bring, and nobody else's.** The built-in lineage has none, on
  purpose: an invented company name is very likely a real company. Without a pool, `Organization` and
  `Product` are labelled. With one, you choose names you know are safe in your market. A pool may hold
  whole names (`"Aurora Servicios"`) or heads with a `companySuffix` / `productSuffix` pool to combine
  with. The loader refuses a company name that the people's pools could also produce — `"Cruz Medina"`
  beside a given name `Cruz` and a family name `Medina` — because it would change where an already
  invented person's name ends, and corrupt a vault that was fine the day before.
- **`language` is recorded, not acted on.** It goes in the fingerprint and the manifest. It does not
  select phonetic rules: the matcher has one coarse Spanish-and-English key, compiled in, and a lineage
  saying `de-DE` gets exactly the same matching as one saying `es-MX`.
- **Patterns replace the built-in pack, they do not extend it.** Two sources for one rule is two rules
  that will eventually disagree.

## Use it from an agent

Silueta ships as an **MCP server** and as an **agent skill**, and the two exist for one reason worth
stating plainly.

**The arguments of a tool call are written by the model.** A tool shaped `redact(text)` requires the
model to have read the document in order to pass it — so by the time the redactor runs, the identified
text is already in the context window, in the conversation history, and in whatever the provider logs.
The tool can return clean text. It cannot un-expose its own input.

So the main tool takes a **path the model never opens**. The server reads the file, redacts it, and
returns the redacted text with a manifest of counts. The values that were removed do not come back, and
neither does the vault's mapping from a person to their invented name.

```jsonc
// claude_desktop_config.json — or any MCP client
{ "mcpServers": { "silueta": { "command": "dnx", "args": ["Silueta.Mcp", "--yes"] } } }
```

| Tool | What it does | Identified text in the model's context |
| --- | --- | --- |
| `redact_transcript` | De-identifies a file on disk against a roster; returns the redacted text and a manifest | **no** — use this one |
| `redact_text` | The same for text passed inline — already exposed by being passed | yes, unavoidably |
| `explain_name_match` | Why the matcher does or does not treat two spellings as one name, for a given kind: keys, edit distance, ratio, threshold — with the verdict taken from the detector itself | no |
| `list_pattern_rules` | The pattern rules, and which Safe Harbor identifiers no rule emits | no |

**There is no re-identification tool, and there will not be one.** The vault is the only artefact that
can undo the work, every use of it is meant to be logged by the person who did it, and a model calling
a tool is not that person.

Keeping that promise takes more than not declaring such a tool, because the capability can hide inside
another one. `redact_transcript` reads whatever path the model writes, and a file that matches no roster
comes back unchanged — so pointing it at the vault returned the whole subject-to-invented-name table,
reported as safe to export. The server therefore:

- **confines every path to one directory** — set `SILUETA_ROOT`, or it uses the directory the server was
  started in;
- **refuses to read anything that looks like a vault**, and refuses a run where the vault is also the
  document or the output;
- **withholds the redacted text** rather than returning it when no roster was given, when nothing was
  replaced, or when the run left residue. The reason comes back in a `withheld` field; `outputPath`
  still writes a clean result to disk.

The skill ([`SKILL.md`](SKILL.md)) is the judgment that goes with those tools: never read a document into
the conversation, never quote its content back, never claim a corpus is de-identified, give the measured
leak rate with its interval when asked, and say what is known to survive. Install it with
`npx skills add peopleworks/Silueta -g`, or as a Claude Code plugin with
`/plugin marketplace add peopleworks/Silueta`. More in [`skill/README.md`](skill/README.md).

## How it works

1. **What you already know comes first.** An organisation knows who its records are about: a home-care
   agency knows its patients, their families and its own staff, and a company knows its clients and its
   catalogue. Matching values you hold beats guessing which capitalised word is a name, and it almost
   never deletes a clinical term by mistake. A recogniser, when one is plugged in, only handles the
   residue.
2. **Names are compared by sound.** A coarse phonetic key shared by Spanish and English collapses the
   confusions that actually happen — b/v, s/z/c, ph/f, y/j, silent h, doubled letters — and an edit
   distance on top absorbs the rest. `Na'vi`, `Navy` and `Navi` are one word here. A window of words
   stops at the end of a sentence, and a short company name has to be the same letters, not only the
   same sound — "Inc" is not "ink".
3. **Shapes are matched by rule.** Phone numbers, e-mail, record numbers, dates, ages over 89: a JSON
   pattern pack, which is a file anyone can extend by pull request, never compiled code.
4. **Replacement follows a policy.** HIPAA Safe Harbor by default: names become consistent invented names,
   dates keep only their year, ages above 89 become "90 or older". Postal codes are removed whole rather
   than kept to three digits — the rule allows three digits only where that area holds more than 20,000
   people, and the census table that decides which is which is not in this package yet.
5. **Every run writes a manifest, and the manifest carries this build's failure rate.** What was removed,
   by kind, by detector, under which policy and which lineage, bound to the text it came from by a hash —
   plus the leak rate measured below, embedded in the assembly so it travels with the corpus and needs no
   network call. An expert determination rests on the method being written down, and a page of counts with
   no error rate beside them is the overclaim this library exists to argue against.
6. **The vault decides the invented names, and remembers them.** One subject, one invented name, across
   every document in the corpus — and a re-identification code that is random rather than derived from
   the person, per 45 CFR § 164.514(c). No invented name is allowed to be one this pipeline would find
   again, so running a redacted document through again changes nothing. The vault never travels with the
   data.
7. **Every run reads its own output back.** After replacing, the same detectors run over the result. If
   they still find anything, the run is reported as unsafe to export and nothing is written. Every other
   rule here is enforced when something is *chosen*, and a rule enforced at choosing time is not the same
   as one that holds at emitting time: a surrogate minted safely for one document is reused in the next,
   whose roster it may well be on. An empty residue proves nothing on its own, since a name no detector
   knows is missing from it too.
8. **The leak rate is the headline number.** Not the share of identifiers removed, which always looks
   good: the share of *documents* with at least one identifier left. At 99% recall per mention, a document
   with fifty mentions leaks about 40% of the time. It is counted in characters and against the redacted
   text, so half a name covered is a name leaked, and a replacement that equals the original is a leak
   rather than a success.

Read [Docs/ALGORITHM.md](Docs/ALGORITHM.md) for the detail, including what each choice costs.

## The number

**Silueta has a measured leak rate, and it is high.** It was measured on thirty synthetic home-care
transcripts — scripts written for the purpose, spoken by Windows voices, degraded like phone calls and
transcribed by Whisper large-v3 — and it is reproduced by `silueta evaluate --gold corpus-synthetic/tts-asr`.
A test runs that evaluation and fails if the table below stops matching it.

<!-- leak-rate:start — written by tools/Silueta.Calibration; PublishedNumberTests checks it against a fresh evaluation -->
| 30 documents · 19 Sep 2026 · engine 0.1.0 · lineage `silueta-core/1` | Silueta | The same roster, matched literally |
| --- | --- | --- |
| **Every kind marked** — could this corpus leave the building? | 90.0% of 30 transcripts (95% CI 74.4%–96.5%) | 96.7% of 30 transcripts (95% CI 83.3%–99.4%) |
| **In scope** — the kinds this build has a way to find | 76.7% of 30 transcripts (95% CI 59.1%–88.2%) · recall 0.854 | 86.7% of 30 transcripts (95% CI 70.3%–94.7%) · recall 0.777 |
| **What matching by sound is worth** — Silueta minus literal, in scope, paired bootstrap over documents | recall +0.077 [+0.041, +0.118] · leak rate -0.100 [-0.233, 0.000] | — |
<!-- leak-rate:end -->

**The first row answers whether this corpus could be shared, and the answer is no.** Almost every
transcript still says something identifying — most often a place, which no rule looks for yet, or a person
the roster never named.

**The second row judges the matcher, and it still fails three transcripts in four.** "In scope" means the
kinds the pattern pack has a rule for plus the kinds on each document's roster; a rule that exists and
fails stays in. The failures are nicknames no roster lists ("Lupita", "Teddy", "Chuy") and pattern rules
that miss what a recogniser writes for dictated numbers — a record number read out as "441729", an age as
"96", an e-mail address as "tuan.nguyen at example.com".

**The third row is the thesis, and it is small.** Matching the roster by sound instead of letter for letter
covers about eight more of every hundred characters in scope, and the interval excludes zero, so it is not
noise. Whether that changes how many transcripts leak, thirty documents cannot say: that interval reaches
zero.

**These numbers moved once since they were first published, and the reason is written down.** The matcher
used to accept a pair of spellings when their keys were 0.84 similar; it now allows a budget of edits read
from the shorter key — none below four characters, one to seven, two above. One edit in a six-letter key
scores 0.833, so the old rule was refusing single-letter damage to names of exactly the length most given
names are. In scope the rate went from 80.0% to 76.7% and recall from 0.834 to 0.854, and the literal
baseline did not move, which is what a change to the matcher and only the matcher looks like. It was not
free: with `Rose` on a roster, "brought roses from the garden" is now redacted too.

What this corpus is, so the numbers are not read as more than they are:

- **Synthetic, and not independent.** The scripts were written by the author of the matcher. They were
  committed before the corpus was first evaluated, and the history shows it.
- **One annotator.** Automatic alignment from script to transcript, every entry read by Claude, no person.
  Agreement between annotators cannot be computed and is not.
- **Voices, not people.** No accents, no crosstalk, no room. Each script was recorded under a single
  condition, so the condition is confounded with the script's content: the per-condition results in the
  report (clean, phone, noisy phone) cannot be read as the effect of the audio.
- **No NER baseline yet.** A literal roster match is the only comparison.

How the corpus was built, and the rules that keep it from being tuned to a result:
[`tools/corpus/README.md`](tools/corpus/README.md).

**What is next, in order.** The matcher work this corpus pointed at — nicknames, and the dictated numbers
the pattern pack misses — judged on data it was not tuned against. A second corpus that records every
script under every condition, and a person as a second annotator. Then the parts of Safe Harbor still
missing: no rule emits a postal code or a street address today, and numbers and dates spoken as words
("five five five, oh one four seven", "September eleventh") are not normalised at all.

### Data that is not a person

The claim at the top of this file is wider than clinical records, and this is what it costs.

*Companies and products are identifier kinds, and the library invents no names for them.* `Organization`
and `Product` exist, and so does `ClientName` — which is a **person**, deliberately: in home care the
client is the patient, and a client that is a company is an `Organization`. A company on the roster comes
back as `[ORGANIZATION]` and a product as `[PRODUCT]`, and the manifest lists both under
`surrogatesUnavailable`, because the policy asked for an invented name and did not get one. That is on
purpose. An invented company name is very likely a *real* company, and putting an uninvolved one inside a
client's call is a different harm from an invented person's name, so this library does not pick those
names for you. Bring a `company` pool in a [lineage](#bring-your-own-dictionaries-the-lineage) and you get
company names back, from a list you know is safe in your market.

*A company is matched in exactly the words the roster gave it.* It is registered whole, never word by
word, so `Acme Corporation` does not turn every "corporation" in the text into an identifier. The price is
that `Acme` said alone is not found, and neither is `Acme Corp`: "Corp" against "Corporation" scores 0.36
against a floor of 0.84, because abbreviation cuts a word short and is not a sound the recogniser
confused. Put each form on the roster as its own entry under the same `subjectId`. And `TurboFresh` on the
roster still does not find `Turbo Fresh` in the text — one word against two — which is the next piece of
matcher work, not this one.

*The lineage exists, and it does not yet cover everything it should.* Pools, labels, generalisations and
pattern rules are yours to bring. Three things are still the library's: `language` does not select
phonetic rules, the kinds themselves are a fixed list, and a generalisation is a literal string — there is
no ladder that derives a wider value from the one it replaces, so `85001 → 850**` is not expressible. That
last one waits on the same census table as the postal-code rule.

## What it is not

- **Not a compliance certificate.** It removes and it measures; whether a corpus may leave a building is a
  decision for a lawyer, and an expert determination is a person signing their name.
- **Not anonymisation, and the distinction is legal, not pedantic.** Under HIPAA Safe Harbor, a record
  with the eighteen identifiers removed stops being PHI and may be shared. Under the GDPR, pseudonymised
  data is *still personal data* (Recital 26) precisely because a vault exists that reverses it. Silueta
  reduces exposure; it does not put a corpus outside the reach of European data-protection law.
- **Not proof that nobody can be recognised.** What identifies a person is not always their name. "The
  94-year-old with this rare condition in this postal code" points at one person with every name in the
  sentence invented, which is why age over 89 and postal codes are generalised — and why a measured leak
  rate, not a promise, is the point of the project.
- **Not a model.** Nothing is downloaded and nothing is uploaded. `Silueta.Core` has no dependencies, so
  it runs offline, inside the environment that is allowed to hold the identified text.
- **Not finished.** See [the number](#the-number).

## What already exists, and what is actually left over

Naming the alternatives is cheaper than being caught not knowing them.

**Speech engines that redact PII inside the ASR** — Amazon Transcribe, Deepgram, AssemblyAI — are the
closest competitor, and the strongest, because they never suffer the problem this library is built
around: they redact from the audio and the lattice, before a name is ever misspelled into text. If your
recogniser offers it, use it. What it does not give you is a roster of the people this record is actually
about, a policy you can read, a vault you hold, or a number for how often it failed — and it ties your
de-identification to one vendor's pipeline.

**General PII redactors** — Microsoft Presidio, Azure AI Language PII (PHI domain), AWS Comprehend
Medical, Philter, scrubadub — work on written text, where a name is spelled the way someone typed it.
Point them at a transcript and they miss `Ellenor Vasques` and delete `Parkinson`.

**Evaluation harnesses** — Presidio Research in particular — already do much of what `silueta evaluate`
does, and are worth borrowing from rather than reinventing.

**[ARX](https://github.com/arx-deidentifier/arx) is not an alternative; it is the stage after this one,**
and it is worth knowing before anyone claims a corpus is safe. ARX anonymises *tables*, and its subject is
the risk this library does not address: once the eighteen identifiers are gone, does a combination of the
remaining attributes still single someone out? It answers with k-anonymity, ℓ-diversity, t-closeness,
δ-presence and differential privacy, and it measures the utility its own transformations destroyed.
Silueta produces the de-identified text and the structured fields that come out of it — age band, region,
date, condition — which is exactly the table ARX evaluates. Silueta's own linkage report counts the
smallest group of subjects that share their surviving details; ARX is where that question is answered
properly.

So what is genuinely Silueta's: matching a roster you already hold *through* recogniser damage, a leak rate
measured per document rather than per mention and published with its interval, and both in a
dependency-free .NET library you can run where the identified text is allowed to be. That is a narrower
claim than "PII redaction", and it is the one this repository can defend.

## Layout

| Path | What it is |
| --- | --- |
| `src/Silueta.Core` | The engine: detectors, lineage, policy, vault, manifest, and the evaluation — leak rate, linkage report. No dependencies. |
| `src/Silueta.Cli` | `silueta demo`, `silueta redact` and `silueta evaluate`, shipped as a dotnet tool. |
| `src/Silueta.Mcp` | The MCP server: four tools, the main one taking a path. |
| `SKILL.md` · `skill/` | The agent skill and how to install it. |
| `corpus-synthetic/` | The gold corpus the published number is measured on. Synthetic, and nothing real is ever committed. |
| `tools/corpus/` | How that corpus was made: scripts, voices, degradation, Whisper, alignment and its review. |
| `tools/Silueta.Calibration` | Re-measures this build against that corpus and writes both copies of the number: the JSON embedded in `Silueta.Core`, and the table above. |
| `src/Silueta.Web` | The browser demo, deployed to GitHub Pages. One page, no server, no HTTP client. |
| `tests/Silueta.Core.Tests` | The tests — including the ones that hold this README's demo and numbers to the code. |
| `Docs/` | `ALGORITHM.md`, every decision and what it costs; and the brand. |

## Contributing

Issues and pull requests are welcome. Three things this repository holds to, so a contribution does too:

1. **Nothing about a real person enters the repository** — not in a test, a corpus, a roster, a lineage or
   an issue. Real corpora belong in `corpus/`, which `.gitignore` keeps out of every commit.
2. **A defect is fixed test-first.** The test fails on the old code before the fix goes in.
3. **The numbers are held to the code.** CI checks that the demo does not leak, that this README shows what
   the demo prints, that the MCP server declares its tools, and — through the test suite — that the leak
   rate above is what a fresh evaluation computes. If your change moves the number, the pull request says so.

## License

MIT — see [LICENSE](LICENSE).

---

<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="Docs/brand/silueta-dark.svg">
  <img src="Docs/brand/silueta.svg" alt="" width="44" height="44">
</picture>

### Built by PeopleWorks

Created by **Pedro Hernández — PeopleWorks**,
[Microsoft MVP for .NET](https://mvp.microsoft.com/en-US/mvp/profile/24060a02-dbc6-44ec-bca5-c213ff9835c5)

Built with [.NET 10](https://dotnet.microsoft.com/) · [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) ·
[faster-whisper](https://github.com/SYSTRAN/faster-whisper) for the evaluation corpus

**PeopleWorks AI tools** — [Signs of AI](https://github.com/peopleworks/SignsofAI) reads what an AI wrote ·
**Silueta** keeps the people out of what you give one

[📐 Algorithm](Docs/ALGORITHM.md) ·
[🧪 Corpus](corpus-synthetic/README.md) ·
[🤖 Agent skill](SKILL.md) ·
[🔌 MCP server](src/Silueta.Mcp/README.md)

**The numbers in this README are checked against the code on every build — including the one that says
the leak rate is high.**

MIT licensed — use it, fork it, ship it.

© 2026 PeopleWorks

</div>
