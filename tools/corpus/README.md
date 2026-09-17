# Corpus generator

Builds `corpus-synthetic/tts-asr/`: thirty synthetic home-care transcripts, spoken by Windows voices, degraded like a
phone call, transcribed by Whisper large-v3, and annotated by aligning the identifiers marked in each script
to what the recogniser actually wrote.

```bash
python tools/corpus/generate.py      # from the repository root
```

The recogniser damage is real — `Okafor` became "aquifer", `Itzel Ramirez` became "He fell Ramirez",
`Nayeli Huerta` became "Jelly Huerta". The voices, scripts and calls are not, and each document says so.

## What it needs

Windows (`System.Speech` voices: Zira and David in English, Sabina in Spanish), `ffmpeg` with `libopus`, and
`faster-whisper` with the `large-v3` model on a CUDA GPU. The CUDA runtime installed as pip packages
(`nvidia-cublas`, `nvidia-cudnn`) is added to the DLL search path by the script. CI does not run this: the
generated documents are committed, and `silueta evaluate` is plain .NET over them.

## The files

| File | What it is |
| --- | --- |
| `scripts.json` | The thirty scripts. `[[Kind\|spoken text]]` marks an identifier, including kinds Silueta has no rule for — places, spoken dates, people not on the roster. Leaving those out would flatter the number. |
| `reviews.json` | Corrections to the automatic alignment, each with its reason. Applied by the generator, so regenerating reproduces the corpus. |
| `say.ps1` | One call to a Windows voice. |
| `generate.py` | Everything else. |
| `work/` | Audio and the alignment review table. Regenerable, not committed. |

## The rules it keeps, because breaking either makes the number meaningless

1. **The conditions were fixed before any document was evaluated** — `clean` (the voice as is), `phone`
   (300–3400 Hz, 8 kHz, Opus at 8 kbps) and `noisy-phone` (the same, plus pink noise and a speaker 10%
   faster). They are not tuned against the matcher's results. Tuning them until the matcher fails more, or
   less, is choosing the number. A new condition is added, never an old one adjusted.
2. **The gold comes from the script, never from anything Silueta produced.** A review decides which
   transcribed words belong to which identifier. It never decides that damaged words stop counting:
   everything the recogniser wrote in place of a name stays marked, because someone who knows the person can
   still recognise "He fell Ramirez".

## What this corpus is not

- **Not independent.** The scripts were written by Claude, who also wrote and changed the matcher, on
  16 Sep 2026, before any of these documents was evaluated. The corpus was committed before its first
  evaluation so that can be checked.
- **Not two annotators.** One pass of automatic alignment, every entry read by Claude, no person. Agreement
  cannot be computed and is not.
- **Not real calls.** Three synthetic voices, no accents, no crosstalk, no room. Real transcripts under an NDA
  would be the test that matters, and they never enter this repository.
