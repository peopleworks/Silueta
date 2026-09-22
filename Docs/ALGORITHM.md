# The algorithm, and what each choice costs

Silueta exists because of one fact about its input: **it is not writing, it is what a speech recogniser
thought it heard.** Every design decision below follows from that.

In one real meeting transcript, taken with Whisper large-v3, the word *Navi* came out as `Na'vi`, `AVI`
and `novel`, and one speaker's *shift* came out as `chief` thirteen times out of fourteen. Any redactor
that searches for a string misses all of it.

## 1. Two lanes

The operational lane — the note the nurse signs, the account record the sales team works from — needs to
know who the subject is. It stays identified, inside the environment allowed to hold it.

Silueta works in the other lane: analysis, evaluation, training corpora, demos. In practice that lane now
ends at somebody else's model. An organisation that wants sentiment over a year of calls, patterns across
a shift roster, a dashboard of what goes wrong and when, has to put the text in front of an AI it does not
run — and the reason to hesitate is almost never the analysis. It is that the text says who. Take the who
out and the question survives intact, because "Patient 1 has high cholesterol" and the same sentence with
a name carry identical statistics and different exposure.

Nothing here is a substitute for a BAA, for a data-processing agreement, or for a lawyer's reading of
where data may live. And nothing here makes a corpus anonymous in the European sense: pseudonymised data
is still personal data under GDPR Recital 26, because the vault exists. Under HIPAA Safe Harbor, a record
with the eighteen identifiers gone stops being PHI. Two regimes, two answers, one pipeline.

## 2. Known values before open recognition

The caller hands Silueta a `DeidentificationContext`: the patient, the family, the staff on the shift, the
phone number on file. Detection starts there.

**Why.** Open-ended entity recognition on clinical text fails in both directions at once: it misses names
it has never seen, and it removes *Parkinson*, *Hodgkin* and *Mayo* because they look like people. A
roster has neither failure. What it needs instead is tolerance for spelling, which is the next section.

This generalises past the clinic, and it is the strongest argument for the whole approach: an
organisation already holds the list. A home-care agency has its patients and its staff; a company has its
client accounts and its catalogue; a firm has its matters. The thing a general-purpose redactor must
guess is the thing its user could have simply handed over.

**A company is registered whole; a person is registered whole and word by word.** Half the mentions of a
person are a first name alone, so every part of "Eleanor Vasquez" goes on the roster under one subject.
The same rule applied to "Acme Corporation" made "Corporation" that client, and every unrelated
corporation in the transcript was replaced — measured, "bought a corporation" came back as "bought a
Guadalupe". So a kind that is not a person is registered only as given. What it costs: "Acme" alone is not
found, and it used to be, but only by accident of the wrong rule. Which kinds are people is answered in
one place, `IdentifierKindExtensions.IsPersonName`, because the roster and the vault both turn on it and
two lists would eventually disagree about one kind.

**What it costs.** Names nobody wrote down — a neighbour, a nickname, the doctor mentioned once — are
invisible to this detector. That residue is what a model-backed detector is for, and until one is plugged
in, the residue shows up in the leak rate, which is the honest place for it.

**The first rule for that residue: a person the transcript names through a relationship.** "My daughter
Linda brought pie" came back with Linda in it, in a partner's test with their own data: the roster held the
patient and the nurse, and nobody lists a patient's daughter. F2.7 had named this on 11 September, before
the corpus existed, with its own order — relationship rules first, measure, and only then decide whether a
model is needed. The rule is built from that text, the kinship words already embedded for the linkage report
(committed before the corpus was frozen), and the partner's sentence; the corpus was not read to shape it.

Its shape is narrow on purpose: a relationship word, then one or two title-cased words, with nothing between
them but a space or a comma. It refuses a word with no lower-case letter ("I", or the `FAMILY` this pipeline
writes), a word with a digit, a title ("Doctor Reyes" is F2.7's third item), a relationship that ends with
its sentence, and a capitalised relationship word with no possessive ("Daughter Linda called"). It does not
hear "Linda, my daughter" or "la hija de la señora Pérez", and tests say so.

**It adds to the roster; it is not a detector.** A name the caller's roster already finds is the roster's —
a listed relative keeps her subject and her invented name. Anything else is added to that run's roster with
no subject, so the matcher finds every mention of it, not only the one after "daughter": "Linda said she
would call" two sentences later is the same leak, and a document that still says Linda once still leaks.
Nobody is invented, because the agency never listed this person and a subject minted from the text would put
a derivative of a real name into the vault's keys; each mention becomes a label, coreference is lost for
them, and the manifest counts them in `unrosteredPeople` and names the rule in `relativesRule`. A relative
who shares a listed person's surname is registered without that surname on its own, so "Mr. Pryor" stays
the patient's. It runs in the literal baseline too, so the comparison between the two stays a comparison of
matching methods.

**What it cost to measure:** nothing moved for Silueta — every relative this corpus names that way is
already on a roster — and the baseline's recall on relatives rose, which narrowed the value of matching by
sound from +0.077 to +0.073. A rule that moves nothing on the corpus it was not shaped on, and fixes the case
it was written for, is a result; not having looked at the corpus is what makes it one.

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
| `g` before `e`/`i` → `y`, the same symbol as `j` | `Jimena` / `Gimena` — one Spanish sound |
| doubled letters collapsed | `Ellenor` / `Eleanor` |
| apostrophes and hyphens dropped | `Na'vi` → `navi` |

Then Levenshtein distance on the keys, with a **budget of edits** per word read from the shorter key: none
below four characters, one from four to seven, two from eight. It used to be a proportion — a similarity of
at least 0.84 — and the comment beside it said that accepted one edit in a six-letter key. It did not: one
edit in six is 0.833, and six letters is the length of an ordinary given name, so the layer that exists to
absorb recogniser damage was dead in the middle of its own range. A proportion is the wrong shape anyway. A
recogniser writes a wrong letter, or two; it does not write a wrong percentage, and scaling the allowance
with length says a longer name may be mangled more when what a longer name really offers is more evidence
that it is the right one. The budget is read from the shorter key so that a three-letter roster entry
cannot inherit an allowance from whatever long word it met. `MatchTolerance` is the only place that knows
this rule, its digest goes into the manifest, and `explain_name_match` quotes its numbers rather than
working them out again.

**One rule deliberately absent:** Spanish `ll` is *not* mapped to the y-sound. Doing it buys `Guillermo`
≈ `Giyermo` and costs `Ellenor` ≈ `Eleanor`, and English doubled letters are judged more common in this
corpus than Spanish *ll*. It is a trade, it is measurable, and nobody has measured it: the corpus that
would settle it does not exist yet.

**What this key is known to get wrong.** `Reyes` and `Rays` — the same surname, as the agency writes it
and as the recogniser heard it — have keys `reyes` and `rais`, three edits apart on a five-character key
that may spend one. The roster does not find it, and no budget that reached this far would still be a
budget: it would reach most surnames. The pairs one edit apart — `Carmen`/`Carmin`, `Javier`/`Xavier`,
`Jimena`/`Gimena` — are found now, and were not under the proportion.

**What the budget cost, measured on the corpus and not guessed.** Recall in scope rose from 0.834 to 0.854
and one transcript of the thirty stopped leaking; the literal baseline did not move, which is how a
change in the matcher and only the matcher looks. Against that, five characters of new over-redaction, all
of them one word: with `Rose` on the roster, "brought roses from the garden" became "brought Remy from the
garden". That is the collision a budget buys, and the answer to it is a roster that can say which of its
words are ordinary words — F2.4, not a smaller budget.

**Why coarser than Double Metaphone.** A precise phonetic key still demands the right consonant, and ASR
damage is not phonetically principled — it substitutes whole words (`shift` → `chief`). A coarse key plus
an edit distance catches more of it. The cost is false positives, which is exactly why this detector only
ever compares against values the caller already knows, never against open text.

**A short key is exact, and for a company that is still not exact enough.** Below four characters only an
exact key match counts, because short keys are where fuzzy matching starts eating real words. For a
person that is the right amount of tolerance — "Ana" and "Anna" share a key and are one patient. For an
organisation or a product it is a collision with the language: the key exists to make different
spellings equal, so "Inc" and "ink" share `ink`, "Zoho" and "so" share `so`, "HP" and "P" share `p`, and a
roster entry for any of them redacted the ordinary word everywhere it appeared. So a short word of a
company or product must also be the same letters, case and accents aside. What it costs: a short brand
the recogniser misspelled is no longer caught, and the residue check cannot see it either. What it does
not decide: whether a brand should be compared by sound at all. "Lyft" still matches "lift" (a four-letter
key) and "Nvidia" still matches "envidia" (0.857); that question is for a corpus, and two tests pin it as
open. `explain_name_match` takes the kind and asks the detector for its verdict rather than re-deriving
it — it used to carry its own copy of these rules, and the first change to them made the copy wrong.

**A name of N words is matched against N adjacent words, and the window stops at a sentence end.** It
used to see only the words, so with "Acme Corporation" on the roster, "We called Acme. Corporation tax is
due next month." came back as "We called Ariel Gaitan tax is due next month." — two sentences fused, a
full stop eaten, a tax replaced. A window is now refused when a `.` `!` `?` or `…` sits between two of its
words, unless the roster value has punctuation in the same place ("St. Mary Hospital") or the word before
it is a single letter ("John F. Kennedy"); and it is refused across a blank line.

It is *not* refused across a single line break, and that asymmetry is the decision. A transcript wrapped
at a fixed width breaks lines wherever the column ends, including inside a name. A person split that way
would still be caught, word by word; a company is registered only whole, so counting a wrap as a
boundary would lose the whole company name. A fused sentence is an ugly false positive; a lost company
name is a leak, and this library takes the first over the second. What it costs: a name the text
punctuates and the roster does not ("Acme Corp. Industries" against "Acme Corp Industries") is no longer
matched as one name.

## 4. Shapes by rule

Phone numbers, e-mail, URLs, IPs, record numbers, numeric and spoken-month dates in both languages, ages
above 89, and a ZIP code introduced as one — after "ZIP", "zip code", "postal code", "código postal" or a
state. Each of those in the form a transcript of speech actually carries as well as the written one: a date
as "September eleventh" or "once de septiembre", an e-mail address as "tuan.nguyen at example.com" or
"jamileth arroba ejemplo punto com", an age as "ninety-four years old" or after the word age. The anchors
are what keep those honest — a month name, a top-level domain, the words "years old" — because a nurse
reading a blood pressure aloud is also a number in words, and that figure is one this library keeps. Not a bare five-digit number, which would read every record number, amount and count as a postal
code; and the span is the digits alone, so the words that introduced them stay. States, the city before one
(§6d), and street addresses (§6e). A JSON pack, embedded but overridable, because a pattern is the kind of thing an agency should be
able to add without a compiler. Each rule carries its own confidence, and every regex runs with a timeout:
the text comes from outside, and so does the pack.

## 5. Resolution

Detectors overlap on purpose — the family name inside an e-mail address, the given name inside the full
name. What survives is a set of spans that do not touch, in reading order, each carrying the id of the
detector that spoke for it.

**Overlapping candidates are united, not ranked and discarded.** Discarding was right for containment and
wrong for everything else. With a roster holding `Ana Maria` and `Maria Perez`, the text `Ana Maria Perez`
gives two candidates where neither contains the other; the longer one won, the other was thrown away
whole, and the characters only it covered — a word of somebody's name — stayed in the transcript. A union
cannot leave a character that a detector found and nothing covers, and that is written down as a test
rather than as a habit.

**Where a span runs and whose it is are two questions, and the honest answer to the second is sometimes
nobody.** A united span keeps a subject when something covering the whole of it names that subject and
nothing else covering it disagrees. That is every ordinary case, including the household where a mother
and daughter share a surname: there the full name *contains* the surname, so the mention is the mother's
and coreference survives. Where two people are called the same thing, or where two names cross, nothing in
the run knows whose mention it is — so the span is replaced with a label instead of an invented name, and
`ambiguousAttributions` in the manifest counts it. Losing the thread on one span is a cost; attributing a
sentence to the wrong person is a different kind of thing.

**And every tie is broken on the candidate's own content** — length, confidence, position, kind, subject,
detector — never on the order the roster was written in. Two roster entries that produced the same span
used to be separated by whichever the sort reached first, so reordering a roster changed whose life a
sentence was about.

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
| `Surrogate` | names of people, organisations, products | a consistent invented name per subject — or, for a kind the lineage brings no pool for, its label (see below) |
| `Label` | phone, e-mail, URL, IP, address, city, record and account numbers | `[PHONE]`, `[EMAIL]`, `[CITY]`, … |
| `YearOnly` | dates | `3/14/2026` → `2026` |
| `Generalize` | ages over 89, postal codes | `94 years old` → `90 or older`, and `born in 1930` with it (§6f); `85004` → `850XX`, or `000XX` where the census counts 20,000 people or fewer behind the prefix (§6c) |
| `Keep` | states | `Flagstaff, Arizona` → `[CITY], Arizona` (§6d) |

**Why surrogates rather than labels for names.** The text stays a sentence, so whatever reads it next —
a person, a model, a metric — still works. Concretely: a model reading `[NAME_1] told [NAME_2] that she
was worried about her mother` has to rebuild a reference chain that the sentence no longer carries,
and coreference is what sentiment, behaviour and timeline analysis are built on. A name carries no
analytic signal of its own, so trading a real one for an invented one costs nothing and keeps everything
else. Shapes are the opposite case — an invented phone number carries no signal either, and it is
somebody's real number — which is why those get labels.

**What it costs, beyond the paragraph below.** A surrogate reads as a fact. A reader who does not know
the corpus is redacted will believe Ale Espinal was there, and a system that ingests the corpus will
happily report on her; labels announce themselves and surrogates do not, which is why every run writes a
manifest and why the MCP tools return counts rather than a clean bill of health. And the stability that
makes cohorts possible — one subject, one name, five hundred transcripts — is the same property that
links documents to each other. That is the point of it and the risk in it, and it is the reason the vault
does not travel with the corpus.

**For an organisation, a surrogate costs something a person's does not.** An invented person's name is
shared by thousands of real people, so it points at nobody; `Urena` is in the pool and is an ordinary
surname, and the project accepts that. An invented company name is close to unique, so it very likely
points at one real company — which the redacted transcript then places on a call it had nothing to do
with. That is why the built-in lineage ships no company or product names at all, and why an
`Organization` with no pool is labelled rather than given a name from the only pool there is (which is
people, and which turned "Acme Corporation" into "Ariel Bravo"). An organisation that wants company
surrogates brings its own list, chosen by someone who knows what is safe in that market. What the library
must *not* do about it, written down so it is not proposed twice: check invented names against a company
or trademark register. That is a network call and a data licence, inside a library whose case rests on
having no dependencies.

The labelled fallback costs coreference, and the cost lands on exactly the corpus these kinds are for:
"[ORGANIZATION] said they would call [ORGANIZATION] back" has merged two companies. It is still the better
failure — "[ORGANIZATION] called about the delay" reads as a company, and "Ariel Bravo called about the
delay" reads as a person and is false.

**Where the words come from.** Not from the code. The pools, the labels and the generalisations are a
**lineage**: a file the organisation writes, loaded the way a pattern pack is loaded and fingerprinted
into the manifest the way the policy already is. The lists this library ships with are a lineage too —
`Lineage/Lineages/lineage.core.json`, embedded in the build — because a default that lives in an array in
the source is a default nobody can replace without forking.

A pool is validated at load, and one of the rules is worth stating here: **no two entries may sound alike
to this matcher**. Two surrogates whose phonetic keys collide are two people the next pass reads as one,
and the vault cannot undo that, because it recorded two distinct names. The same check that keeps a
surrogate away from the roster is turned on the pool itself.

What the lineage still does not decide: the kinds (a closed enum — see the README's Status), the phonetic
rules (`language` is recorded, not acted on), and any generalisation that has to *derive* a wider value
from the original rather than state a literal one. That last is the hierarchy idea from ARX. The only rung
anyone has asked for — three digits of a postal code — is compiled in with its census table (§6c) rather
than written in the lineage. What a lineage *can* add is `values` — kinds to values that identify in every
record, the towns an organisation serves, found by the roster's matcher and never a person — and `lists`,
word lists its own rules name as the built-in rules name theirs. That last one is what makes the library
portable at all: the rules that ship are American and Mexican, and without it a project in another country
had to spell its own country into every regular expression it wrote, with no way to say "the same list as
the other rule". A name the build already uses is refused, because one name must mean one list.

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

## 6a. Whose rules: policies as data

Safe Harbor is a floor, and a strict one for analysis: only the year of a date, and of a place only the
state and, where the area is large, three digits of the ZIP. Pedro's principle for this library is that dates, ages, diagnoses, measurements and places are the
statistics an analysis is made of, and what matters is not knowing whose they are — so an organisation can
write, in its lineage, a policy of its own under a name of its own.

Three rules keep that from becoming a way to weaken a corpus quietly:

- **Safe Harbor lives in code, once.** It is the default everywhere and needs no file. A copy of its table
  in the embedded lineage would have been the second copy of a rule, and the name `safe-harbor` is reserved
  so that no file can put it on a manifest over other rules — the same failure the immutable policy was
  built against, reached by editing a text file instead of a dictionary. Its fingerprint did not change.
- **A policy is a set of departures, not a table.** A kind it does not name keeps Safe Harbor's action, so
  a forgotten kind removes more. Actions are read by name only — `Enum.TryParse` would have read `"4"` and
  `"Label, Keep"` as `Keep`, the lesson E5 taught for kinds — and one it does not know stops the load.
- **The departures travel.** The manifest lists each one (`departuresFromSafeHarbor`), the caveat every report
  carries repeats them with the HIPAA consequence, and the lineage's fingerprint covers its policies — but
  only when it has some, so every lineage written before this, the built-in one included, digests exactly as
  it did.

The policy is chosen by name, by the operator: `--policy` on the command line, `SILUETA_POLICY` for the MCP
server, where a model that could choose would choose the policy that keeps everything.

## 6b. Reading the output back

After replacing, the same detectors run over the finished text. What they find is the run's **residue**,
it goes in the manifest, and anything but zero means the transcript must not be exported — the CLI exits
non-zero and the MCP tools say so in their own result.

The read-back runs against the run's roster, which includes the relatives the transcript named, so the
output is searched for "Linda" and not for whatever stands after "daughter". That is also why the
relationship rule is not a detector: one that fired on position would find the pipeline's own invented
names ("my daughter Chris") and report every one as residue.

**Idempotence has one chosen exception.** An invented name standing right after a relationship — "her
daughter Noa", where Noa was invented for a listed relative — is, on a second pass, a relationship and a name
nobody listed, and becomes a label. Skipping names the vault already invented would keep the second pass
still, and would leave a real son called Noa in the text, silently, the day a patient's invented name happens
to be his. Not skipping loses an invented name instead of exposing a real one, and in that collision the
read-back finds Noa and the run refuses to export.

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

## 6c. Three digits of a ZIP: the census table

Safe Harbor keeps the first three digits of a ZIP code where the area formed by every ZIP sharing them holds
more than 20,000 people "according to the current publicly available data from the Bureau of the Census",
and turns the rest into 000 (45 CFR § 164.514(b)(2)(i)(B)). The rule needs a table, and the obvious one is
the wrong one.

- **Not HHS's seventeen.** The de-identification guidance (26 November 2012, §3.1) prints seventeen prefixes
  from the 2000 census and, in the same paragraph, says not to rely on them "if more current data has been
  published". Against the 2020 count that list keeps six prefixes the rule says to zero — 202, 204, 205,
  369, 753, 772 — and zeroes five that may stay, Las Vegas's 890 among them.
- **Derived, not copied.** `tools/census/zip3.py` takes table P1 (total population) of the 2020 Census
  Demographic and Housing Characteristics File for every ZIP Code Tabulation Area and sums it by prefix —
  HHS's own method, on the current count. The file it writes records the URL, the day it was fetched and the
  SHA-256 of what the Census Bureau served. The populations add up to that response's total as the file
  records it, and a test holds them to it, so one number edited by hand shows.
- **An allow-list.** 894 prefixes have at least one tabulation area, and 876 of them hold more than 20,000
  people. The other 106 of the thousand — military mail, unassigned ranges, ZIPs that are only post-office
  boxes — have nobody the census counted, and they become 000 too. A list of small prefixes would have
  passed each of them for the reason that nobody wrote it down.
- **Compiled in.** Like Safe Harbor's table of actions, and for the same reason: a lineage that could
  rewrite it could keep every prefix under a manifest that says safe-harbor. A lineage's
  `generalizations.PostalCode` is skipped and reported; an organisation that wants the whole code gone
  writes `"PostalCode": "Label"` in a policy, where it is listed as a departure. The manifest names the
  table and its digest in `postalCodeTable`, and a manifest from before it says `none` — which is true of
  those runs: the whole code went.
- **What it does not do.** The 2020 counts carry the Census Bureau's disclosure-avoidance noise, and one
  prefix, 576, sits 310 people above the line; the rule says to follow the published count, and this does.
  A ZIP nobody introduced as one is not found, and one spoken as words is not read. And the table is the
  United States count while the anchor is also Spanish: a Mexican "código postal 06600" is widened against
  it, and three of its digits may stay under a manifest that says the US census decided. Safe Harbor is US
  law and `phone-us` already sets that precedent, but it is written down here and in every report's caveat
  rather than left to be discovered.

## 6d. Places: the city goes, the state stays

Safe Harbor removes "all geographic subdivisions smaller than a state". Until `safe-harbor/0.3` neither a
city nor a state was a kind, so both survived for the same reason — nothing looked — which was right about
the state by accident and wrong about the city, and a manifest could say neither. Now `City` is labelled
and `State` is kept: a row that says `Keep`, not an absence, so the fingerprint describes it.

- **A city is found two ways.** A rule anchored on the state that follows it — "Flagstaff, Arizona", "Mesa,
  AZ 85201", "Las Cruces, Nuevo México" — because a capitalised word before a state is a place far more
  often than anything else, where a capitalised word alone is usually a person. It needs capitals, so a
  recogniser that writes everything in lower case gives it nothing; a handful of words that open a sentence
  ("Yes, Arizona") are excluded. And the organisation's own list, `values` in its lineage, found by the
  roster's matcher, lower case and mishearings included. The built-in lineage lists no town: the places the
  frozen corpus marks are mostly towns named alone, and a list written from it would tune the number to it.
- **A state is found in order to be counted.** Full names anywhere, in English and Spanish; a two-letter code
  only after a place ("Mesa, AZ") or before a ZIP, because OK, OR, IN, ME, HI, LA, DE and CO are words in
  one language or the other. Safe Harbor keeps it, and a kept reading is dropped *before* overlaps are
  resolved — so a patient called Georgia is never kept because Georgia is also a state, and "New York, NY"
  becomes `[CITY], NY` because the kept reading of New York gives way to the city.
- **The states are one list.** Three rules need them — the state, the city before one, the ZIP after one —
  and written out three times they would drift. `lists.core.json` holds them once and a rule names
  `{{us-state}}` or `{{us-state-code}}`; the pattern fingerprint is taken over the expansion, so a list that
  changes changes the digest. A rule naming a list the build does not have is skipped and reported.
- **An organisation keeps both in a policy of its own**: `"City": "Keep"`, listed as a departure.

Measured on the frozen corpus: no change, and it could not have been otherwise — the corpus names no state.
What speaks for this section is its tests.

## 6e. Street addresses: the standards, and nothing else

Safe Harbor names the street address first among the subdivisions smaller than a state. The rules that find
one are taken from the two standards that define what an address is, and every word list they use is cited
in `lists.core.json`:

- **USPS Publication 28.** A house number, an optional directional (§233), one to three capitalised words or
  an ordinal ("23rd"), a street suffix from Appendix C1 spelled out or abbreviated, and an optional unit from
  Appendix C2 — "221 N Main St, Apt 4B". Without a house number, only a suffix that names a road and nothing
  else in ordinary English, spelled out: "Maple Street", but never "Supreme Court", and never an
  abbreviation, because `St` is also Saint and `Dr` also Doctor. A post-office box too.
- **INEGI, Norma Técnica sobre Domicilios Geográficos.** A type of road — calle, avenida, privada, calzada,
  andador — a capitalised name with its particles ("Privada de los Pinos"), an optional number ("#8",
  "número 1450"). The type of road may be lower case, as Spanish writes it; the name may not.
- **Capitals are the anchor**, as for the city. "A 5 minute drive" and "a 3 block walk" are a number, a word
  and a street suffix; only the capitals tell them from "5 Minute Drive". A lower-case transcript gives
  these rules nothing, and that is pinned by a test.

Measured on the frozen corpus, and to be read with a caveat: the corpus's four street marks were seen before
these rules were written, at the close of §6d. Pooled recall rose from 0.769 to 0.808 and the whole-corpus
leak rate fell from 90.0% to 86.7%, with over-redaction unchanged. In scope the rate rose from 76.7% to
83.3%, because `Address` entered the scope and the corpus marks most places as towns named alone, which a
street rule cannot see. Both are reported; neither is tuned away.

## 6f. A birth year is an age

Safe Harbor allows the year of a date and then takes it back once: "all ages over 89 and all elements of dates
(including year) indicative of such age" may be kept only "aggregated into a single category of age 90 or
older" (§ 164.514(b)(2)(i)(C)). The pack found the age said as a number — "94 years old" — and nothing found
the same fact said as a date, so "born in 1930" came back with its year under a manifest that said
safe-harbor.

- **It is not a rule about shapes**, which is why it is not in the pack: whether 1930 is over 89 depends on
  when you ask. The reference is the record's own date when the caller sets `RecordedOn`, and the day of the
  run otherwise; the manifest says which in `ageReference`, because the same transcript redacted in two
  different years is two different corpora.
- **The test is blunt on purpose**: reference year minus birth year, 90 or more. A birthday nobody stated
  falls either side of the run, and the side that removes is the side to fall on. Born in 1936, read in 2026:
  the year goes. Born in 1937: it stays, because 89 is the most she can be.
- **It reads what the pack already found.** A date near a word about birth — born, birthday, nació,
  nacimiento — is re-read as an age over 89 and replaced whole, so no second rule argues with the date rule
  about the same span; a bare year, which no date rule matches because a year alone is not a date, becomes a
  span of its own. It never reads across the end of a sentence: "a birth defect. She moved in 1930" is not a
  date of birth, and that year is one Safe Harbor allows.
- **One shape of a year in the build.** The rule that takes a year away and the rule that keeps one are the
  same regular expression, in one place, for the reason this file keeps repeating.

Not measured on the frozen corpus: it names one birth at all ("her birthday is August 9") and no birth year.
What speaks for this section is its tests.

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
match type.

**The policy's name and the counts by kind are two different lists, and a reader has to be told so.** The
name is the standard under which the eighteen identifiers were removed. The counts are everything that
was removed, which since `safe-harbor/0.2` can include kinds the standard does not name — an
organisation, a product. Safe Harbor is a floor: removing more keeps the name true, and keeping something
it names would not. And `surrogatesUnavailable` lists the kinds the policy asked to replace with an
invented name that were labelled instead, because the lineage had no pool; a label where the policy said
surrogate is a decision the run made, and a manifest quiet about it describes a policy that did not
happen.

Plus the four things that bind it to something:

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
- **`measuredLeakRate`.** How often the build that wrote the manifest is known to leave an identifier
  behind — a property of the build, not of this document, and the sentence says so. Every other field
  here says what ran; none of them said how often what ran is wrong, and this is the file a compliance
  officer opens. Its default is the admission that there is no measurement, because a field left empty
  reads as a run that did not leak. See §9.
- **`postalCodeTable`.** Which census count decided the ZIP prefixes a run kept (§6c). Two corpora
  redacted against different counts keep different prefixes under one policy fingerprint.
- **`ageReference` and `birthYearRule`.** Which date decided that a birth year was an age over 89, and where
  that date came from (§6f). Same reason: a year kept under one reference is removed under another.

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

**A rate carries its interval.** A 95% Wilson interval, not the textbook normal one: with thirty documents
and no leaks the normal interval is [0%, 0%] and prints as a proven zero, while Wilson says the true rate
could still be one in nine. `7.5% of 40 transcripts (95% CI 2.6%–19.9%)` is what a small corpus can
honestly support, and the interval is part of the string so it cannot be quoted without it.

**By kind of identifier and by detector.** Every score carries both breakdowns, with two attribution rules
chosen rather than defaulted. What was sensitive, covered or left behind counts under the kind the
*annotators* gave it — a patient's name the detector called OtherName is still a covered patient's name.
What was removed without need counts under the kind the *detector* gave it, because that is the rule that
fired. Where annotations of two kinds overlap, the shared characters count under both, so per-kind figures
can add up to more than the total, which stays the total. The per-detector figures are attribution, not
ablation: a character two detectors both covered counts for each, and only a second run without a detector
says what would have been lost.

**Across documents, keyed by invented name.** Every other measure here is per document, and the attack is
not. The vault gives a subject one invented name across the corpus — which is what makes a dashboard
possible, and which makes the name a join key: every visit, every kept year, every "90 or older", every
"her daughter", filed under one string. `Linkage.Analyze` counts, per invented name, the documents it
appears in and the quasi-identifiers that survived beside it, and reports the smallest group of subjects
sharing one pattern — k, in k-anonymity. It is keyed by the invented name, which the corpus already
carries, never by subject id or code. And it writes into itself what it cannot see: places (no rule finds
them yet, so all survive), clinical detail (the analysis this library preserves is also a
quasi-identifier), wording outside its vocabulary, and the fact that it attributes every quasi-identifier
in a document to every subject named there — pessimistic by construction. The kinship and age wording it
counts is embedded data, like the lineage, not code.

The gold set is the yardstick, and a redactor scored against its own output measures nothing.

**It has produced a number, and the number is in the README with its corpus, its intervals and its
limits.** Thirty synthetic transcripts with real recogniser damage; most leak whichever way it is counted.
Two things about how it is reported are decisions and belong here. First, there are two leak rates, and
they are not interchangeable: over every kind the annotators marked, which answers whether the corpus could
be shared and includes kinds no rule looks for; and over the kinds the build has a way to find — the
kinds the pattern pack has a rule for, plus each document's roster — which judges the matcher. The scope is
derived from the build, not written down, so a rule that exists and fails still counts against it.
Second, the value of matching by sound is reported as a *difference* from the literal baseline over the
same documents, with an interval from a paired bootstrap that resamples documents rather than characters:
characters within one document are not independent — a name said five times is one decision made five
times — and an interval that pretended otherwise would be narrower than thirty documents can support. The
seed is fixed, so the interval is part of what the repository reproduces.

**The baseline is the point of the comparison, and it is built to be exactly as dumb as a ten-minute
job.** `DenyListDetector` matches the same roster literally — folding case and accents, whole words only,
first names registered the way the roster registers them — and hears nothing. Folding matters: a literal
baseline that missed "SOFIA" for "Sofía" would be a straw man, and a thesis that beats a straw man has
proven nothing. On the demo it finds none of the names at all, because every one of them was damaged by the
recogniser; its recall equals the pattern pack's on its own. That is one document and it is not a result.
It is the shape the result will have.

**The number travels inside the build, not only on the page.** `tools/Silueta.Calibration` runs the
evaluation over the committed corpus and writes two things in the same run: `published-leak-rate.json`,
embedded in `Silueta.Core`, and the table in the README. Both, in one run, because a number is exactly the
kind of rule this project keeps finding a second, disagreeing copy of — and the drift would be in the
direction that flatters. `PublishedLeakRate.Current` reads the embedded copy: counts and dates, never
percentages, so `LeakRateEstimate` stays the only place that knows how a rate and its Wilson interval are
computed. From there the figure reaches the manifest, the caveat every MCP tool returns, and the last line
of `silueta demo`, with no corpus, no file path and no network call. A build with no measurement embedded
still builds, and says in those words that it has none rather than quoting somebody else's.

What the code cannot do for you is the **motivated intruder test**: someone who knows the clients reads
the redacted transcripts and tries to name them. For a small agency in one city, that test is the one that
decides whether a corpus is really de-identified.
