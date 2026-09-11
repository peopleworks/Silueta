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

Two things in that output are on purpose. `Rays` survived, and so did the nickname `Ellie` — nobody wrote
either of them down anywhere. **That is what the leak rate is for**: this library measures how often it
fails instead of promising that it doesn't.

## Try it

```bash
dotnet run --project src/Silueta.Cli -- demo
```

```bash
silueta redact --in shift-042.txt --context roster.json \
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
6. **Re-identification codes are random.** The vault maps a subject to an opaque id that is not derived
   from the person — 45 CFR § 164.514(c) — and it never travels with the data.
7. **The leak rate is the headline number.** Not the share of identifiers removed, which always looks
   good: the share of *transcripts* with at least one identifier left. At 99% recall per mention, a
   transcript with fifty mentions leaks about 40% of the time. That arithmetic is the reason this library
   measures itself.

Read [Docs/ALGORITHM.md](Docs/ALGORITHM.md) for the detail, including what each choice costs.

## What it is not

- **Not a compliance certificate.** It removes and it measures; whether a corpus may leave a building is a
  decision for a lawyer, and an expert determination is a person signing their name.
- **Not a model.** Nothing is downloaded and nothing is uploaded. `Silueta.Core` has no dependencies, so
  it runs offline, inside the environment that is allowed to hold the identified text.
- **Not finished.** Nicknames, relationship cues ("my daughter", "Dr. —"), spoken numbers and dates,
  address parsing, and a pluggable model detector are all ahead. So is the browser demo.

## Layout

| Path | What it is |
| --- | --- |
| `src/Silueta.Core` | The engine: detectors, policy, vault, manifest, leak rate. No dependencies. |
| `src/Silueta.Cli` | `silueta demo` and `silueta redact`, shipped as a dotnet tool. |
| `tests/Silueta.Core.Tests` | Every case in them is real speech-recognition damage, not invented. |

## License

MIT. See [LICENSE](LICENSE).
