# Measuring matching cost

Run from the repository root, with the .NET 10 SDK:

```powershell
dotnet build tools/Silueta.Performance -c Release
dotnet run --project tools/Silueta.Performance -c Release --no-build -- --iterations 15
```

This dependency-free probe generates synthetic text, uses a roster of 30 known values, warms each case
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

Every result includes a SHA-256 of the ordered detections: positions, kind, subject, confidence and
match classification. The engine case also hashes its output. Compare those fields before treating
two runs as equivalent; a faster detector that silently finds less is not an improvement.

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
values, and this probe does not establish the cost of the full pipeline with dense overlapping
detections. Cancellation and limits for large inputs remain separate work.
