"""
Builds the tts-asr gold corpus: synthetic scripts, spoken by a Windows voice, degraded like a phone call,
transcribed by Whisper large-v3, and annotated by aligning each marked identifier in the script to what the
recogniser actually wrote.

Why this exists: the one corpus that tests Silueta's thesis is text a recogniser damaged, and the only way
to have that without anyone's data is to make the recogniser damage invented text. The damage is real
recogniser damage. The voices, the scripts and the conditions are not real calls, and every document says
so in its own "annotation" block.

Two rules this script keeps, because breaking either would make the number meaningless:

1. The degradation conditions were fixed before any document was evaluated, and are not tuned against the
   matcher's results. Tuning them until the matcher fails more, or less, is choosing the number.
2. The gold spans come from the script, not from anything Silueta produced. The alignment maps each marked
   identifier onto the transcript; spans whose mapping is uncertain are flagged for review, and the review
   corrects alignment, never coverage.

Requires: Windows (System.Speech voices), ffmpeg with libopus, faster-whisper with the large-v3 model, a CUDA
GPU (the CUDA DLLs installed as pip packages are added to the DLL path below). Run from the repository root:

    python tools/corpus/generate.py
"""

from __future__ import annotations

import difflib
import glob
import json
import os
import re
import site
import subprocess
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools" / "corpus"
WORK = TOOLS / "work"
OUT = ROOT / "corpus-synthetic" / "tts-asr"

MARK = re.compile(r"\[\[(\w+)\|([^\]]+)\]\]")

VOICES = {"en": ["Microsoft Zira Desktop", "Microsoft David Desktop"], "es": ["Microsoft Sabina Desktop"]}

# Fixed before any document was evaluated. Do not tune these against results; add a new condition instead.
CONDITIONS = ["clean", "phone", "noisy-phone"]
PHONE_BAND = "highpass=f=300,lowpass=f=3400,aresample=8000"


@dataclass
class Marked:
    kind: str
    start: int
    end: int


def parse(script: str) -> tuple[str, list[Marked]]:
    """The spoken text, and where each marked identifier sits in it."""
    spoken, marks, cursor = [], [], 0
    length = 0
    for match in MARK.finditer(script):
        before = script[cursor:match.start()]
        spoken.append(before)
        length += len(before)
        value = match.group(2)
        marks.append(Marked(match.group(1), length, length + len(value)))
        spoken.append(value)
        length += len(value)
        cursor = match.end()
    spoken.append(script[cursor:])
    return "".join(spoken), marks


def tokens(text: str) -> list[tuple[int, int, str]]:
    """Whitespace-separated words with edge punctuation trimmed, as (start, end, normalised)."""
    result = []
    for match in re.finditer(r"\S+", text):
        start, end = match.start(), match.end()
        while start < end and not text[start].isalnum():
            start += 1
        while end > start and not text[end - 1].isalnum():
            end -= 1
        if start < end:
            result.append((start, end, normalise(text[start:end])))
    return result


def normalise(word: str) -> str:
    folded = unicodedata.normalize("NFD", word.lower())
    return "".join(c for c in folded if unicodedata.category(c) != "Mn" and c.isalnum())


def align(script: str, marks: list[Marked], transcript: str) -> list[dict]:
    """Maps each marked identifier in the script onto the transcript."""
    s_tok, t_tok = tokens(script), tokens(transcript)
    matcher = difflib.SequenceMatcher(None, [t[2] for t in s_tok], [t[2] for t in t_tok], autojunk=False)
    opcodes = matcher.get_opcodes()

    results = []
    for mark in marks:
        words = [i for i, (s, e, _) in enumerate(s_tok) if s < mark.end and e > mark.start]
        if not words:
            results.append({"kind": mark.kind, "flag": "unmarkable"})
            continue

        first, last = words[0], words[-1] + 1
        targets: set[int] = set()
        flag = None

        for tag, i1, i2, j1, j2 in opcodes:
            if i2 <= first or i1 >= last:
                continue
            if tag == "equal":
                for k in range(max(i1, first), min(i2, last)):
                    targets.add(j1 + (k - i1))
            elif tag == "replace":
                targets.update(range(j1, j2))
                if i1 < first or i2 > last:
                    # The recogniser rewrote the identifier together with a neighbouring word: the transcript
                    # span may include that neighbour. A reviewer decides.
                    flag = "boundary"
            elif tag == "delete":
                flag = flag or "partly-deleted"

        entry: dict[str, object] = {
            "kind": mark.kind,
            "script": script[mark.start:mark.end],
        }

        if not targets:
            entry["flag"] = "deleted"
            results.append(entry)
            continue

        start = t_tok[min(targets)][0]
        end = t_tok[max(targets)][1]
        entry.update({"start": start, "length": end - start, "transcribed": transcript[start:end]})
        if flag:
            entry["flag"] = flag
        results.append(entry)

    return results


def apply_reviews(document_id: str, transcript: str, alignment: list[dict], corrections: list[dict]) -> None:
    """Applies a reviewer's corrections to the alignment. Fails loudly when a correction no longer fits."""
    for correction in (c for c in corrections if c["documentId"] == document_id):
        candidates = [a for a in alignment if a["kind"] == correction["kind"] and a.get("script") == correction["script"]]
        if len(candidates) < correction["occurrence"]:
            raise SystemExit(f"{document_id}: review names '{correction['script']}' #{correction['occurrence']}, which the script does not have.")
        entry = candidates[correction["occurrence"] - 1]

        start, found = -1, 0
        while found < correction["transcribedOccurrence"]:
            start = transcript.find(correction["transcribed"], start + 1)
            if start < 0:
                raise SystemExit(
                    f"{document_id}: review expects '{correction['transcribed']}' #{correction['transcribedOccurrence']} "
                    "in the transcript and it is not there. The transcript changed; review it again.")
            found += 1

        entry.update({"start": start, "length": len(correction["transcribed"]),
                      "transcribed": correction["transcribed"], "flag": "reviewed", "review": correction["why"]})


def run(command: list[str]) -> None:
    subprocess.run(command, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)


def degrade(source: Path, condition: str, seed: int) -> tuple[Path, dict]:
    target = WORK / f"{source.stem}.{condition}.wav"
    if condition == "clean":
        run(["ffmpeg", "-y", "-i", str(source), "-ac", "1", "-ar", "16000", str(target)])
        return target, {"condition": "clean", "filter": "none; mono 16 kHz"}

    encoded = WORK / f"{source.stem}.{condition}.opus"
    if condition == "phone":
        run(["ffmpeg", "-y", "-i", str(source), "-af", PHONE_BAND, "-c:a", "libopus", "-b:a", "8k", str(encoded)])
        detail = {"condition": "phone", "filter": PHONE_BAND, "codec": "opus 8 kbps"}
    else:
        graph = (f"[0:a]atempo=1.1,highpass=f=300,lowpass=f=3400[v];"
                 f"anoisesrc=color=pink:amplitude=0.08:seed={seed}[n];"
                 f"[v][n]amix=inputs=2:duration=first:weights=1 1,aresample=8000[m]")
        run(["ffmpeg", "-y", "-i", str(source), "-filter_complex", graph, "-map", "[m]",
             "-c:a", "libopus", "-b:a", "8k", str(encoded)])
        detail = {"condition": "noisy-phone", "filter": graph, "codec": "opus 8 kbps", "noiseSeed": seed}

    run(["ffmpeg", "-y", "-i", str(encoded), "-ac", "1", "-ar", "16000", str(target)])
    return target, detail


def cuda_dlls() -> None:
    """The CUDA runtime ships as pip packages whose DLLs are not on the search path by default."""
    for packages in site.getsitepackages():
        for directory in glob.glob(os.path.join(packages, "nvidia", "*", "bin")):
            os.add_dll_directory(directory)
            os.environ["PATH"] = directory + os.pathsep + os.environ["PATH"]


def main() -> int:
    WORK.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)

    cuda_dlls()
    import ctranslate2
    import faster_whisper
    from faster_whisper import WhisperModel

    model = WhisperModel("large-v3", device="cuda", compute_type="float16")
    asr = {
        "model": "large-v3", "engine": f"faster-whisper {faster_whisper.__version__}",
        "ctranslate2": ctranslate2.__version__, "computeType": "float16",
        "beamSize": 5, "temperature": 0.0, "conditionOnPreviousText": False, "vad": False,
    }

    scripts = json.loads((TOOLS / "scripts.json").read_text(encoding="utf-8"))["documents"]
    corrections = json.loads((TOOLS / "reviews.json").read_text(encoding="utf-8"))["corrections"]
    per_language: dict[str, int] = {}
    review = []

    for index, document in enumerate(scripts):
        language = document["language"]
        n = per_language.get(language, 0)
        per_language[language] = n + 1
        voice = VOICES[language][n % len(VOICES[language])]
        condition = CONDITIONS[index % len(CONDITIONS)]

        spoken, marks = parse(document["script"])
        raw = WORK / f"{document['id']}.wav"
        run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(TOOLS / "say.ps1"),
             "-Text", spoken, "-Voice", voice, "-Out", str(raw)])

        audio, degradation = degrade(raw, condition, seed=index + 1)
        segments, _ = model.transcribe(str(audio), language=language, beam_size=5, temperature=0.0,
                                       condition_on_previous_text=False, vad_filter=False)
        transcript = " ".join(segment.text.strip() for segment in segments).strip()

        alignment = align(spoken, marks, transcript)
        apply_reviews(document["id"], transcript, alignment, corrections)

        unresolved = [a for a in alignment if a.get("flag") in ("boundary", "unmarkable")]
        if unresolved:
            print(f"  REVIEW NEEDED in {document['id']}: " +
                  "; ".join(f"{a['kind']} '{a.get('script')}' -> '{a.get('transcribed')}'" for a in unresolved), flush=True)

        spans = [{"start": a["start"], "length": a["length"], "kind": a["kind"], "annotator": "claude-aligned"}
                 for a in alignment if "start" in a]

        gold = {
            "documentId": document["id"],
            "source": f"tts-asr/{condition}",
            "language": language,
            "speaker": voice,
            "text": transcript,
            "roster": document["roster"],
            "spans": spans,
            "annotation": {
                "method": ("Spans derived by aligning each identifier marked in the script to the recogniser's "
                           "transcript (difflib over normalised words). Every alignment was read by Claude; "
                           "corrections are in tools/corpus/reviews.json and decide which transcribed words "
                           "belong to which identifier, never whether damaged words count. No person has "
                           "reviewed these spans."),
                "annotators": ["claude-aligned"],
                "humanReviewed": False,
                "script": spoken,
                "tts": {"engine": "System.Speech (SAPI)", "voice": voice, "rate": 0},
                "degradation": degradation,
                "asr": asr,
                "alignment": alignment,
            },
        }

        (OUT / f"{document['id']}.json").write_text(
            json.dumps(gold, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")

        for a in alignment:
            review.append(f"{document['id']:<10} {condition:<12} {a['kind']:<12} "
                          f"{a.get('script', ''):<45} -> {a.get('transcribed', '(none)'):<45} {a.get('flag', '')}")
        print(f"{document['id']} [{condition}, {voice}] {transcript}", flush=True)

    (WORK / "review.txt").write_text("\n".join(review) + "\n", encoding="utf-8")
    print(f"\nWrote {len(scripts)} documents to {OUT.relative_to(ROOT)}; alignment review in {WORK.relative_to(ROOT)}/review.txt")
    return 0


if __name__ == "__main__":
    sys.exit(main())
