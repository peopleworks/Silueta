# Measuring matching and pipeline cost

Run from the repository root, with the .NET 10 SDK:

```powershell
dotnet build tools/Silueta.Performance -c Release
dotnet run --project tools/Silueta.Performance -c Release --no-build -- --iterations 15
```

This dependency-free probe generates synthetic text, uses the roster reported for each case, warms each case
twice, and reports median elapsed time and managed bytes allocated on the calling thread. Input
generation, warmups, hashing and JSON output are outside the measurements. Allocation is cumulative
per call, **not peak memory**. Run comparisons sequentially, with builds and tests finished; runtime
tiering, garbage collection and other processes affect time. These are local measurements, not a
performance guarantee or a simulation of real four-hour recordings.

Each case contains 40,000 words:

| Case | What it exercises |
| --- | --- |
| `detector/repeated` | Eight repeated clinical words, no detections |
| `detector/distinct` | 40,000 distinct synthetic spellings, exposing cache overhead |
| `detector/matches` | Repeated exact and damaged names, 32,727 detections including overlapping candidates |
| `engine/repeated` | The full default pipeline, including pattern rules and the residual pass, with no detections |
| `engine/matches-pinned` | The same 32,727 name candidates resolved into 10,909 replacements, with three assigned surrogates |
| `engine/matches-mint` | 20,000 name candidates resolved into 10,000 replacements; a fresh engine/vault mints the first surrogate |
| `engine/matches-crossed` | Six roster values yield 40,002 candidates and 6,667 ambiguous unions of names belonging to two subjects |

Every result includes a SHA-256 of the ordered detections: positions, kind, subject, confidence and
match classification. Engine cases also hash their output. The audit hash covers detections, output,
residue and every manifest field except the run timestamp. All warmups and measured calls must agree
on that hash, and engine cases must have zero residue; the probe fails otherwise. CI runs one measured
call per case to check these invariants, without a timing threshold. Compare those fields before treating
two runs as equivalent; a faster detector that silently finds less is not an improvement.

The pinned case assigns `Ale Bravo`, `Sol Castro` and `Paz Duarte` before timing. The minting case uses
the default lineage/detectors with a synthetic vault pool containing only `Ale` and `Bravo`; it includes
engine/vault construction and detector checks for the freshly minted name. This makes the text reproducible
while leaving production randomness untouched. It measures one successful mint, not pool exhaustion or
the distribution of retries in larger pools. The crossed case repeatedly presents `Ana Maria Perez`
against `Ana Maria` and `Maria Perez`, with six known values. It must label each union and report lost
attribution rather than assign it to either subject. Raw known-value candidates are counted outside timing;
engine detections are the spans applied after policy filtering and overlap resolution.

## Local comparison, 2 October 2026

Windows x64, .NET 10.0.10, 16 logical processors, Release, 15 measured calls per case. The baseline is
`5e12153` with this same probe copied into an isolated checkout; the changed build caches phonetic keys
within each call and computes edit distance only within the matching budget. Raw results are in
[`results/before.json`](results/before.json) and [`results/after.json`](results/after.json).

| Case | Before (ms) | After (ms) | Before (MiB allocated) | After (MiB allocated) |
| --- | ---: | ---: | ---: | ---: |
| `detector/repeated` | 88.9 | 42.1 | 91.61 | 4.14 |
| `detector/distinct` | 99.7 | 66.7 | 89.31 | 23.99 |
| `detector/matches` | 94.1 | 47.4 | 99.08 | 8.04 |
| `engine/repeated` | 421.9 | 331.3 | 194.12 | 19.24 |

All detection hashes match, including all 32,727 candidates in the case with names, and the engine's
output hash matches. The committed corpus's published recall, leak rate and over-redaction checks also
remain unchanged. No matching threshold, rule fingerprint, lineage or policy changed.

The repeated-word case allocates 95.5% less in the detector and the full pipeline allocates 90.1% less.
Distinct spellings still need their keys computed and stored: the cache itself costs memory, although
removing the edit-distance row allocations more than offsets it in this case. The cache lives only
inside `ComputeAll`; it retains no vocabulary across records. Accepted distances remain exact so their
confidence is unchanged, and public explanations still calculate distances beyond the budget.

This addresses key reuse and bounded edit distance from F2.8. Matching still visits words × roster
values. The follow-ups below cover processing controls and the full pipeline with dense overlaps.

## Follow-up: processing controls

The engine now has configurable input/candidate ceilings and cooperative cancellation. The same
15-call local probe still produces exactly the detection and output hashes above; raw results are in
[`results/processing-limits.json`](results/processing-limits.json). The three detector cases measured
49.8, 71.8 and 53.1 ms, with 4.14, 23.99 and 8.04 MiB allocated respectively; the full engine case
measured 329.4 ms and 19.22 MiB. Checkpoints and iterator wrappers add work, and these local timings
also include runtime and scheduling variation. The allocation improvements persist. That four-case
measurement predates the dense-overlap cases below.

## Follow-up: dense overlaps and replacement allocation

The expanded seven-case probe was run sequentially against `551e047` in an isolated checkout and
the changed build, with the same probe source, runtime and 15-call settings. Raw reports:
[`results/dense-before.json`](results/dense-before.json) and
[`results/dense-after.json`](results/dense-after.json).

| Case | Before (ms) | After (ms) | Before (MiB allocated) | After (MiB allocated) |
| --- | ---: | ---: | ---: | ---: |
| `detector/repeated` | 52.2 | 50.1 | 4.14 | 4.14 |
| `detector/distinct` | 73.7 | 77.2 | 24.00 | 24.00 |
| `detector/matches` | 51.8 | 53.8 | 8.04 | 8.04 |
| `engine/repeated` | 363.1 | 334.8 | 19.22 | 19.21 |
| `engine/matches-pinned` | 466.1 | 425.8 | 29.66 | 27.58 |
| `engine/matches-mint` | 407.2 | 390.5 | 27.54 | 25.97 |
| `engine/matches-crossed` | 321.1 | 294.3 | 25.41 | 25.40 |

All detection, output and audit hashes match across builds. Every engine case has zero residue.
The crossed case preserves all 6,667 ambiguous attributions. No matching, overlap, policy or
surrogate-generation rule changed.

Replacement fitting now asks the existing tokenizer to count up to two words without materializing
tokens. It uses the same scanner, including Unicode marks, supplementary letters, damaged surrogate
pairs and inner apostrophes/hyphens. It allocates no list or word strings, stops when the required count
is known, and observes cancellation. The public tokenizer still returns complete tokens and original
UTF-16 offsets. The fitted surrogate retains the pool's multiword head rules.

This saves 7.0% of managed allocation in the pinned case and 5.7% in the fresh-vault case. A separate
10,000-mention engine regression test fails on the old build at 10,942,816 allocated bytes and passes
below 9,500,000 on the changed build. Tests also compare fitted output with full tokenization for
Unicode/joiner fixtures and 500 deterministic mixed sequences.

The allocation reduction is the supported improvement here. Elapsed times moved in both directions
between preliminary runs, including cases unaffected by replacement fitting, so this does not establish
a CPU-speed improvement. Dense candidate processing is now measured, but synthetic repeated phrases do
not establish performance for arbitrary audio transcripts, large rosters or exhausted surrogate pools.
