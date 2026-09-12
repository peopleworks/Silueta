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
≈ `Giyermo` and costs `Ellenor` ≈ `Eleanor`, and English doubled letters are judged more common in this
corpus than Spanish *ll*. It is a trade, it is measurable, and nobody has measured it: the corpus that
would settle it does not exist yet.

**What this key is known to get wrong.** `Reyes` and `Rays` — the same surname, as the agency writes it
and as the recogniser heard it — have keys `reyes` and `rais`, three edits apart, a similarity of 0.40.
The threshold is 0.84, so the roster does not find it. The threshold also refuses several pairs that are
one edit apart, because one edit in a six-letter key scores 0.833: `Carmen`/`Carmin`, `Jimena`/`Gimena`,
`Javier`/`Xavier`. And `j` and soft `g` are mapped to different symbols (`y` and `h`) although they are
one sound in Spanish, which is what separates that second pair. These are measured numbers, not
suspicions, and they are the first thing the corpus is for.

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

**What a detection does not carry is the text it matched.** Offsets, kind, length, detector, confidence
— nothing else. The result of a redaction is the object a caller is most likely to log, return from an
API or attach to a ticket, and while it held the matched text, that object was a list of every identifier
in the transcript, in a printable record. Whoever legitimately needs the value has the original text in
hand and can slice it; whoever does not, should not receive it by accident. The same reasoning runs
through the rest of the pipeline: the record id must be opaque because the manifest travels, and a
pattern pack that times out raises an exception naming the rule and nothing else, because the
framework's own timeout exception carries the input that defeated it.

## 6. Replacement

| Action | Applied to | Result |
| --- | --- | --- |
| `Surrogate` | names | a consistent invented name per subject |
| `Label` | phone, e-mail, URL, IP, address, record and account numbers | `[PHONE]`, `[EMAIL]`, … |
| `YearOnly` | dates | `3/14/2026` → `2026` |
| `Generalize` | ages over 89, postal codes | `94 years old` → `90 or older`; a postal code is removed whole |

**Why surrogates rather than labels for names.** The text stays a sentence, so whatever reads it next —
a person, a model, a metric — still works. That is the whole of the argument, and it is enough.

**What it is not.** This used to add that a leak would no longer stand out among plausible invented
names. That claim does not survive an adversary. The pool is forty-three words in a public MIT
repository and embedded in the shipped assembly, so subtracting it from the capitalised tokens of a
redacted transcript leaves the leaks. On this repository's own README example the subtraction returns
`Rays` and `Ellie` — precisely the two leaks the README names in prose, found without reading the prose.
At corpus scale the pool is not even needed: twenty-one given names across hundreds of subjects makes
surrogates the *repeated* names and leaks the *singletons*, and a frequency sort separates them. If that
property is ever wanted, it needs a pool of thousands loaded like a pattern pack; until then, surrogates
buy readability and nothing else.

**The vault chooses the surrogate, and writes it down.** It used to be computed — a hash of the subject
id and a policy seed — and a computed surrogate fails three ways at once. It can return the person's own
name: a real *Ale* was handed back "Ale" and counted as redacted. It collides, so two people in one
corpus become one person. And it is never recorded, so the code the vault minted and the name in the text
have nothing to do with each other and nothing can be undone. Now the vault mints it, stores it beside
the re-identification code, and a name in use is never handed out twice.

Three constraints on what it may mint:

- **Not anyone on the roster, and not anything that *sounds* like them.** Equality is not enough, because
  the matcher is phonetic: a surrogate that merely sounds like a roster name is found and replaced again
  on the next pass, and a corpus that changes every time it is reprocessed cannot be reproduced by
  anyone. Re-running Silueta over its own output is a test, and it must be a no-op.
- **Given names are exhausted before any is reused.** Half the mentions in a transcript are a first name
  alone, so while unused ones remain, two subjects sharing "Alex" would make *Alex said* a sentence about
  either of two people. Past twenty-one subjects in one corpus, given names do repeat and only the full
  pair stays unique — two people can share a first name, as they do in life. That costs readability,
  never privacy.
- **The invented names read as either gender on purpose.** Choosing by gender would mean inferring the
  gender of a real person from their name, which is a guess the library has no business making, and a
  wrong guess writes *her son Marta* into a clinical note.

## 6b. Reading the output back

After replacing, the same detectors run over the finished text. What they find is the run's **residue**,
it goes in the manifest, and anything but zero means the transcript must not be exported — the CLI exits
non-zero and the MCP tools say so in their own result.

This exists because every other rule in this library is enforced at the moment something is *chosen*, and
a rule enforced at choosing time is not the same as a rule that holds at emitting time:

- A surrogate is checked against the roster of the record it was minted for. The vault then keeps it, on
  purpose — stability wins, because re-minting would rewrite a corpus behind the caller — and hands the
  same name to every later record. `Urena` is in the surrogate pool and is also an ordinary surname; the
  record where a real Urena appears is not the record the name was cleared against.
- The check runs on the candidate alone, while the matcher scores windows of consecutive words in the
  finished transcript. A one-word surrogate can join the word after it and spell someone real.

It is the same correction the leak meter needed, one level up: check the result, not the decision. And it
has the same honest limit — residue is what *this* pipeline can see, so a name it never knew about is
missing from here too. It is a self-consistency check, not a leak rate.

## 7. The vault

Two things per subject, held together: the **code** a structured field refers to (`SIL-3f9a…`) and the
**surrogate** the transcript says instead of the name. Both random, never derived from the person,
because 45 CFR § 164.514(c) requires that a re-identification code not be derived from or related to
information about the individual — which rules out the hash of a name or a date of birth that so many
pipelines reach for. Codes are 128 bits and checked for uniqueness when minted: 64 bits is inside
birthday range for a corpus of any size, and two subjects sharing a code is two people becoming one
person in every table downstream.

The vault is the only artefact that can undo the work, so it stays where the identified data is allowed
to be, it never travels with the corpus, and every re-identification is the caller's to log. It is also
the thing that has to survive: it is read before a run and written afterwards, through a temporary file
moved over the original, because a process killed halfway through a direct write leaves a truncated vault
— and a truncated vault is a set of people who can no longer be identified by the one party entitled to
identify them. There is no second copy by design.

## 8. The manifest

Record id, policy name, version and **fingerprint**, engine version, counts by kind, by detector and by
match type — plus the four things that bind it to something:

- **`inputSha256` and `outputSha256`.** Without them the manifest is bound to nothing at all, and a
  reviewer holding a corpus and a manifest cannot say the two belong together. `textLength` was the
  closest thing and is a coincidence away from matching.
- **A fingerprint per detector.** The policy was fingerprinted and the rules that do the *finding* were
  not, so two corpora could carry one policy name and have been searched with different patterns.
- **The rules a build could not load, by id.** A pack naming a kind this version does not know is
  skipped rather than crashing a run — in silence, that made "that rule found nothing" and "that rule
  never ran" the same absent key.
- **`keptKinds`.** A corpus redacted with `StaffName = Keep` produces counts identical to a transcript
  with no staff in it.

What is still missing, and worth saying: **a run cannot be reproduced from the manifest.** Invented names
are minted at random, so the only way to reproduce one is to hold the vault — which must not travel. The
fingerprint tells two runs apart; it does not let a third party repeat one.

The fingerprint is a digest of everything that changes what a run did: the action for each kind, the
confidence floor, the policy's own name and version. Two corpora can both be labelled `safe-harbor/0.1`
and have been redacted under different rules — someone edited a copy, or a later version of this library
changed a default. The name is what a human writes down; the fingerprint is what actually ran, and it is
the field to compare before merging two corpora or reproducing a result. It is also why a policy is
immutable: a shared mutable default let one consumer write `Actions[Phone] = Keep` and leave every
manifest afterwards still claiming Safe Harbor.

The phonetic and fuzzy counts are worth reading on their own: they say how much ASR damage a corpus
actually has, which is a property of the recogniser and the accents in the room, not of this library.

## 9. The leak rate

```
leak rate = transcripts with at least one surviving identifier / transcripts
```

Not the share of mentions removed. A transcript with one surviving name is an identified transcript, and
per-mention recall hides that: at 99% per mention, a transcript with fifty mentions leaks about 40% of the
time — 1 − 0.99⁵⁰ ≈ 0.40.

**Counted in characters, and against the redacted text, folding case and accents.** All of that matters,
and the scorer got every part of it wrong until recently:

- **Characters, not spans.** A detection that clipped half a name used to count as a cover — gold
  `Sofía Reyes`, redactor reached `Sofía`, recall 1.0, `Reyes` still in the file. And one detection
  swallowing the whole document used to score zero over-redaction, because it overlapped the only gold
  span there was. Overlapping spans are unioned before anything is counted, so neither is possible.
- **Against the output, not the offsets.** A scorer given only offsets is checking that a detector fired,
  which is not the question; the question is whether the words are gone. A span can be detected,
  replaced, counted — and still be there, because the surrogate equalled the original, or because the
  same name was said again somewhere nobody annotated. So the scorer takes both texts and looks.
- **Span by span, not on the union of the annotations.** Two annotators marking one passage at different
  granularities is the normal case. Merging them first asks only whether the widest reading survived:
  gold `Sofía Reyes` merged with gold `Reyes`, output `Ale Reyes`, and the surname goes unnoticed.
- **Folding case and accents, through the same rule the rest of the library uses.** The survival check
  compared ordinally while `MatchKind.Exact` is defined as "letter for letter, ignoring case and
  accents". So `Sofía` surviving as `Sofia` read as a clean transcript and `SOFÍA` did not — the meter's
  private copy of the equality rule was strict in the one direction that hides leaks.
- **With the punctuation trimmed off an annotation.** Annotators select sloppily, and a span that swept
  up the sentence-final full stop would otherwise leave one uncovered "sensitive" character that
  identifies nobody. At thirty annotations a document that reports a leak in nearly every document, and
  a meter that cries wolf is as useless as one that stays quiet.

An empty corpus has **no** leak rate, rather than a leak rate of zero. The old signature returned 0.0,
which any report would print as a perfect score for a run that measured nothing. A rate now travels with
the number of transcripts it was measured on, because a rate without its denominator is the kind of
number this library exists to stop people publishing.

Alongside it: recall by identifier type, over-redaction (characters removed that the annotators never
marked), and per-detector contribution. The gold set is a random sample annotated by two people, and it
is the yardstick — a redactor scored against its own output measures nothing.

**None of this has been run yet.** The scorer exists and is tested; the gold corpus, the `evaluate`
command and the baselines do not. Everything above describes how the number will be computed, not a
number anyone has.

What the code cannot do for you is the **motivated intruder test**: someone who knows the clients reads
the redacted transcripts and tries to name them. For a small agency in one city, that test is the one that
decides whether a corpus is really de-identified.
