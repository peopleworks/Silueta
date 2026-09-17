# Gold corpus

The yardstick `silueta evaluate` measures against. One JSON file per transcript.

**Nothing in this directory is about a real person, and nothing ever may be.** Real transcripts, even
redacted, even under an NDA, never enter this repository. Every document here is synthetic, and says how
it was made. A real corpus goes in `corpus/`, which `.gitignore` keeps out of every commit; this directory
has a different name so that nobody drops real transcripts beside synthetic ones by habit.

```bash
silueta evaluate --gold corpus-synthetic --out report.json
```

## A document

```jsonc
{
  "documentId": "gold-001",            // unique across the corpus
  "source": "readme-demo",             // where the text came from; reports break results down by it
  "language": "en",
  "speaker": null,                     // for splits by speaker, when a corpus has them
  "text": "Shift report. Sophia Rays was with …",
  "roster": [                          // who the document is about, as the organisation writes them
    { "value": "Sofía Reyes", "kind": "StaffName", "subjectId": "staff-1" }
  ],
  "spans": [                           // what the annotators marked, as offsets into "text"
    { "start": 14, "length": 11, "kind": "StaffName", "annotator": "claude" }
  ],
  "annotation": {                      // how the spans were produced — for the reader; not interpreted
    "method": "…", "annotators": ["claude"], "humanReviewed": false
  }
}
```

The loader refuses, by file and position and never by quoting the text: a span outside the text, a kind
it cannot read, a span with no annotator, a roster entry with no `subjectId`, two documents with one id.
Each of those would still produce a number, which is why each is an error.

## What the numbers mean, and do not

- **One annotator measures that annotator.** Agreement between annotators (Cohen's kappa over characters,
  per kind) needs two. A document marked by one person — or by a model — is reported as such, and no second
  annotator is invented to make agreement computable.
- **A synthetic corpus written by the people who wrote the matcher is not an independent test.** It is how
  a number can exist before real data arrives, and every report says what its corpus is before it says
  anything else.
- **Mixed sources are read per source first.** A transcript a recogniser damaged and one somebody typed do
  not belong in the same average.

## What is here

| Source | Documents | What it is |
| --- | --- | --- |
| `readme-demo` | 1 | The README's demo transcript, marked by hand. The README says "Rays" and "Ellie" survive the demo; this document is the evaluator's canary for that claim, and a test fails if the evaluator disagrees. |
| `tts-asr/clean` | 10 | Synthetic home-care transcripts (5 English, 5 Spanish), spoken by a Windows voice and transcribed by Whisper large-v3. |
| `tts-asr/phone` | 10 | The same kind of script, degraded like a VoIP call before transcription: 300–3400 Hz, 8 kHz, Opus at 8 kbps. |
| `tts-asr/noisy-phone` | 10 | As `phone`, plus background noise and a speaker 10% faster. |

The thirty `tts-asr` documents hold 153 marked identifiers: 54 patient names, 34 staff names, 11 family
names, 4 other people, 14 places, 12 dates, 10 ages over 89, 8 phone numbers, 4 record numbers, 2 e-mail
addresses. How they were made, and the rules that keep them from being tuned to a result, are in
[`tools/corpus/README.md`](../tools/corpus/README.md). They were committed before they were first evaluated.
