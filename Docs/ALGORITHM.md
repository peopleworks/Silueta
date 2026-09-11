# The algorithm, and what each choice costs

Silueta exists because of one fact about its input: **it is not writing, it is what a speech recogniser
thought it heard.** Every design decision below follows from that.

In one real meeting transcript, taken with Whisper large-v3, the word *Navi* came out as `Na'vi`, `AVI`
and `novel`, and one speaker's *shift* came out as `chief` thirteen times out of fourteen. Any redactor
that searches for a string misses all of it.

## 1. Two lanes

The operational lane — the note the nurse signs — needs to know who the patient is. It stays identified,
inside the environment allowed to hold it. Silueta works in the other lane: analysis, evaluation,
training corpora, demos. Nothing here is a substitute for a BAA or for a lawyer's reading of where data
may live.

## 2. Known values before open recognition

The caller hands Silueta a `DeidentificationContext`: the patient, the family, the staff on the shift, the
phone number on file. Detection starts there.

**Why.** Open-ended entity recognition on clinical text fails in both directions at once: it misses names
it has never seen, and it removes *Parkinson*, *Hodgkin* and *Mayo* because they look like people. A
roster has neither failure. What it needs instead is tolerance for spelling, which is the next section.

**What it costs.** Names nobody wrote down — a neighbour, a nickname, the doctor mentioned once — are
invisible to this detector. That residue is what a model-backed detector is for, and until one is plugged
in, the residue shows up in the leak rate, which is the honest place for it.

## 3. Sound, not letters

`PhoneticKey` reduces a word to a coarse spelling of how it sounds, shared by Spanish and English:

| Rule | Why |
| --- | --- |
| accents stripped, case folded | `Sofía` and `Sofia` are one name |
| `v` → `b`, `w` → `b` | the distinction does not exist in Spanish and recognisers drop it |
| `z`, `ce/ci` → `s` | `Vasquez` / `Vasques` |
| `ph` → `f` | `Sophia` / `Sofía` |
| `qu` → `k`, `c` → `k` | `Quique` / `Kike` |
| `j` → `y`, `y` before a vowel → `y`, otherwise `i` | `Jamileth` / `Yamilet`, `Navy` / `Navi` |
| `h` silent unless it follows `c` | `Herrera` / `Errera` |
| `g` before `e`/`i` → `h` | `Gilberto` / `Hilberto` |
| doubled letters collapsed | `Ellenor` / `Eleanor` |
| apostrophes and hyphens dropped | `Na'vi` → `navi` |

Then Levenshtein distance on the keys, with a threshold of 0.84 per word and an exact-match floor for keys
shorter than four characters.

**One rule deliberately absent:** Spanish `ll` is *not* mapped to the y-sound. Doing it buys `Guillermo`
≈ `Giyermo` and costs `Ellenor` ≈ `Eleanor`, and in this corpus English doubled letters are far more
common than Spanish *ll*. It is a trade, it is measurable, and the harness is how you would revisit it.

**Why coarser than Double Metaphone.** A precise phonetic key still demands the right consonant, and ASR
damage is not phonetically principled — it substitutes whole words (`shift` → `chief`). A coarse key plus
an edit distance catches more of it. The cost is false positives, which is exactly why this detector only
ever compares against values the caller already knows, never against open text.

## 4. Shapes by rule

Phone numbers, e-mail, URLs, IPs, record numbers, numeric and spoken-month dates in both languages, ages
above 89. A JSON pack, embedded but overridable, because a pattern is the kind of thing an agency should be
able to add without a compiler. Each rule carries its own confidence, and every regex runs with a timeout:
the text comes from outside, and so does the pack.

## 5. Resolution

Detectors overlap on purpose — the family name inside an e-mail address, the given name inside the full
name. The longest span wins, then the most confident. What survives is a set of spans that do not touch,
in reading order, each carrying the id of the detector that found it.

## 6. Replacement

| Action | Applied to | Result |
| --- | --- | --- |
| `Surrogate` | names | a consistent invented name per subject |
| `Label` | phone, e-mail, URL, IP, address, record and account numbers | `[PHONE]`, `[EMAIL]`, … |
| `YearOnly` | dates | `3/14/2026` → `2026` |
| `Generalize` | ages over 89, postal codes | `94 years old` → `90 or older`, `85018` → `850XX` |

**Why surrogates rather than labels for names.** The text stays a sentence, so whatever reads it next —
a person, a model, a metric — still works. And a name that slipped past the redactor no longer stands out
among the brackets: with everything else replaced by plausible names, a leak is not advertised to whoever
is skimming.

The surrogate is chosen from a hash of the **subject id and the policy seed** — never of the real name.
Hashing the name would make the replacement a function of the thing being hidden.

The invented names read as either gender on purpose. Choosing by gender would mean inferring the gender of
a real person from their name, which is a guess the library has no business making, and a wrong guess
writes *her son Marta* into a clinical note.

## 7. The vault

Subject → opaque id, random, minted on first use: `SIL-3f9a…`. Random rather than a hash of a name or a
date of birth, because 45 CFR § 164.514(c) requires that a re-identification code not be derived from or
related to information about the individual. The vault is the only artefact that can undo the work, so it
stays where the identified data is allowed to be, and every re-identification is the caller's to log.

## 8. The manifest

Record id, policy name and version, engine version, counts by kind, by detector and by match type. The
phonetic and fuzzy counts are worth reading on their own: they say how much ASR damage a corpus actually
has, which is a property of the recogniser and the accents in the room, not of this library.

## 9. The leak rate

```
leak rate = transcripts with at least one surviving identifier / transcripts
```

Not the share of mentions removed. A transcript with one surviving name is an identified transcript, and
per-mention recall hides that: at 99% per mention, a transcript with fifty mentions leaks about 40% of the
time — 1 − 0.99⁵⁰ ≈ 0.40.

Alongside it: recall by identifier type, over-redaction (spans removed that the annotators never marked),
and per-detector contribution. The gold set is a random sample annotated by two people, and it is the
yardstick — a redactor scored against its own output measures nothing.

What the code cannot do for you is the **motivated intruder test**: someone who knows the clients reads
the redacted transcripts and tries to name them. For a small agency in one city, that test is the one that
decides whether a corpus is really de-identified.
