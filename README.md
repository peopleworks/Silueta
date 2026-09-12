# Silueta

**De-identification for conversation transcripts — the text a speech recogniser produced, not the text
someone typed.**

[![License: MIT](https://img.shields.io/github/license/peopleworks/Silueta?color=blue)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

A silhouette keeps the shape and loses the face. That is the whole idea: the clinical content of a visit
survives intact — symptoms, medications, vitals, what was escalated — and the people in it do not.

```
Shift report. Sophia Rays was with Mrs. Ellenor Vasques this morning.
Her daughter Jamileth called at 602-555-0147 about the 3/14/2026 appointment,
and Ellie said she would email jamileth.v@example.com. The patient is 94 years old.
Blood pressure 138 over 82, pain 4 out of 10, and she took the warfarin with breakfast.
```

```
Shift report. Yael Rays was with Mrs. Ale Espinal this morning.
Her daughter Chris called at [PHONE] about the 2026 appointment,
and Ellie said she would email [EMAIL]. The patient is 90 or older.
Blood pressure 138 over 82, pain 4 out of 10, and she took the warfarin with breakfast.
```

Not one of those names was spelled the way the agency spells it. `Sofía Reyes` arrived as `Sophia Rays`,
`Eleanor Vasquez` as `Ellenor Vasques`, `Yamilet` as `Jamileth`. A redactor that looks for names as they
are written on the roster misses every one, and a missed name is a leak.

**And two names are still in that output, which is the more useful half of the example.**

`Rays` survived, and `Reyes` is on the roster four lines above — this is not a name nobody knew, it is a
name the matcher failed on. Their phonetic keys are `reyes` and `rais`: three edits apart, a similarity
of 0.40 against a threshold of 0.84. `Ellie` survived for a different reason: it is a nickname, `eleanor`
against `elie` scores 0.29, and nothing in the roster or the rules knows that Eleanors are called Ellie.
One is a matcher that needs work; the other is a category the library does not handle yet. Both are
counted as failures, neither is a design decision, and the difference between them is the kind of thing a
leak rate is supposed to tell you.

This library is built to measure how often it fails rather than to promise that it doesn't. **That number
does not exist yet** — see [Status](#status) before you rely on anything here.

## Try it

```bash
dotnet run --project src/Silueta.Cli -- demo
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
each transcript invents new ones for the same people.

## How it works

1. **What you already know comes first.** An agency knows its patients, their families and its own staff.
   Matching values you hold beats guessing which capitalised word is a name, and it almost never deletes a
   clinical term by mistake. A recogniser, when one is plugged in, only handles the residue.
2. **Names are compared by sound.** A coarse phonetic key shared by Spanish and English collapses the
   confusions that actually happen — b/v, s/z/c, ph/f, y/j, silent h, doubled letters — and an edit
   distance on top absorbs the rest. `Na'vi`, `Navy` and `Navi` are one word here.
3. **Shapes are matched by rule.** Phone numbers, e-mail, record numbers, dates, ages over 89: a JSON
   pattern pack, which is a file anyone can extend by pull request, never compiled code.
4. **Replacement follows a policy.** HIPAA Safe Harbor by default: names become consistent invented names,
   dates keep only their year, ages above 89 become "90 or older", ZIPs keep three digits.
5. **Every run writes a manifest.** What was removed, by kind, by detector, under which policy version.
   An expert determination rests on the method being written down.
6. **The vault decides the invented names, and remembers them.** One subject, one invented name, across
   every transcript in the corpus — and a re-identification code that is random rather than derived from
   the person, per 45 CFR § 164.514(c). No invented name is allowed to sound like anyone on the roster,
   so running a redacted transcript through again changes nothing. The vault never travels with the data.
7. **The leak rate is the headline number.** Not the share of identifiers removed, which always looks
   good: the share of *transcripts* with at least one identifier left. At 99% recall per mention, a
   transcript with fifty mentions leaks about 40% of the time. It is counted in characters and against
   the redacted text, so half a name covered is a name leaked, and a replacement that equals the original
   is a leak rather than a success.

Read [Docs/ALGORITHM.md](Docs/ALGORITHM.md) for the detail, including what each choice costs.

## Status

**Silueta has not yet measured its own leak rate.** The scorer is in the box and tested; the gold corpus
it needs, the `evaluate` command that would run it, and the baselines that would make the result mean
something are not written. Until they are, this library is a redactor with a plan, and the honest reading
of the example above is that two of the names in it survived.

What is in place: the roster matcher, the pattern pack, the Safe Harbor policy, the vault, the manifest,
and the leak-rate scorer. What is next, in order: the corpus and `silueta evaluate`; then the matcher
changes that corpus will judge; then the parts of Safe Harbor still missing — no rule emits a postal code
or a street address today, and spoken numbers and dates ("five five five, oh one four seven",
"September eleventh") are not normalised at all.

## What it is not

- **Not a compliance certificate.** It removes and it measures; whether a corpus may leave a building is a
  decision for a lawyer, and an expert determination is a person signing their name.
- **Not a model.** Nothing is downloaded and nothing is uploaded. `Silueta.Core` has no dependencies, so
  it runs offline, inside the environment that is allowed to hold the identified text.
- **Not finished.** See [Status](#status).

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

**Evaluation harnesses** — Presidio Research in particular — already do much of what Phase 1 below needs,
and are worth borrowing from rather than reinventing.

So what is genuinely Silueta's: matching a roster you already hold *through* ASR damage, a leak rate
measured per transcript rather than per mention, and both in a dependency-free .NET library you can run
where the identified text is allowed to be. That is a narrower claim than "PII redaction", and it is the
one this repository can defend.

## Layout

| Path | What it is |
| --- | --- |
| `src/Silueta.Core` | The engine: detectors, policy, vault, manifest, leak rate. No dependencies. |
| `src/Silueta.Cli` | `silueta demo` and `silueta redact`, shipped as a dotnet tool. |
| `tests/Silueta.Core.Tests` | Every case in them is real speech-recognition damage, not invented. |

## License

MIT. See [LICENSE](LICENSE).
