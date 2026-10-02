"""Freeze synthetic candidates and validate separate blind annotations; never invoke the engine."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import shutil
import zipfile

HERE = Path(__file__).resolve().parent
PROTOCOL = HERE / "protocol.json"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON property.")
        result[key] = value
    return result


def load(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    except (json.JSONDecodeError, UnicodeError):
        raise ValueError("Invalid JSON or text encoding.") from None


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode("utf-8")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write(path, value):
    Path(path).write_bytes(encoded(value))


def identifier(value):
    return isinstance(value, str) and re.fullmatch(r"[a-z][a-z0-9-]{0,63}", value) is not None


def candidates(value, protocol):
    require(set(value) == {"studyId", "provenance", "documents"}, "Unexpected candidate fields; annotations are not allowed.")
    require(value["studyId"] == protocol["studyId"], "Wrong study id.")
    provenance = value["provenance"]
    require(identifier(provenance.get("authorId")) and provenance.get("authorType") in {"model", "human"}, "Missing author identity/type.")
    require(provenance.get("synthetic") is True and provenance.get("consultedImplementation") is False
            and provenance.get("consultedEngineOutput") is False, "Candidates must be synthetic and authored blind.")
    docs = value["documents"]
    require(isinstance(docs, list) and len(docs) == protocol["documents"], "Wrong document count.")
    ids, counts, owners = set(), Counter(), {}
    for doc in docs:
        require(set(doc) == {"documentId", "caseGroup", "scenario", "source", "language", "recordedOn", "text", "roster"}, "Unexpected document fields.")
        doc_id = doc["documentId"]
        require(identifier(doc_id) and doc_id not in ids, "Invalid or duplicate document id.")
        ids.add(doc_id)
        require(identifier(doc["caseGroup"]), "Invalid case group.")
        require(doc["scenario"] in protocol["scenarios"] and doc["language"] in {"en", "es"}, "Unknown scenario/language.")
        require(doc["source"] == "synthetic-manual" and doc["recordedOn"] == protocol["recordedOn"], "Wrong source or record date.")
        require(isinstance(doc["text"], str) and 1 <= len(doc["text"]) <= 10000, "Invalid transcript length.")
        doc["text"].encode("utf-16-le")  # Reject unpaired surrogate code points.
        require(isinstance(doc["roster"], list), "Roster must be an array.")
        for entry in doc["roster"]:
            require(set(entry) == {"value", "kind", "subjectId"}, "Unexpected roster fields.")
            require(isinstance(entry["value"], str) and entry["value"].strip()
                    and entry["kind"] in protocol["kinds"] and identifier(entry["subjectId"]), "Invalid roster entry.")
            # Related records are kept in one case group, never split into independent observations.
            owner = owners.setdefault(entry["subjectId"], (entry["value"], doc["caseGroup"]))
            require(owner == (entry["value"], doc["caseGroup"]), "A subject changed identity or crossed case groups.")
        counts[(doc["scenario"], doc["language"])] += 1
    require(all(counts[(s, language)] == n for s in protocol["scenarios"]
                for language, n in protocol["languagesPerScenario"].items()), "Scenario/language balance differs from protocol.")
    return value


def freeze(input_path, output, package, commit):
    protocol = load(PROTOCOL)
    data = candidates(load(input_path), protocol)
    require(re.fullmatch(r"[0-9a-f]{40}", commit) is not None, "Engine commit must be a full Git SHA.")
    # Record the actual Core bytes bundled in the CLI tool package, without executing any code.
    with zipfile.ZipFile(package) as archive:
        core = archive.read("tools/net10.0/any/Silueta.Core.dll")
        cli = archive.read("tools/net10.0/any/silueta.dll")
    lock = {"schemaVersion": 1, "stage": "candidates-frozen-not-annotated", "engineCommit": commit,
            "cliPackageSha256": sha(Path(package).read_bytes()), "coreAssemblySha256": sha(core),
            "cliAssemblySha256": sha(cli), "candidatesSha256": sha(encoded(data)),
            "protocolSha256": sha(PROTOCOL.read_bytes()),
            "instructionsSha256": sha((HERE / "ANNOTATION.md").read_bytes())}
    lock["studySha256"] = sha(encoded(lock))
    output = Path(output)
    output.mkdir(parents=True, exist_ok=False)  # Never silently replace a frozen study.
    write(output / "candidates.json", data)
    shutil.copyfile(PROTOCOL, output / "protocol.json")
    shutil.copyfile(HERE / "ANNOTATION.md", output / "ANNOTATION.md")
    shutil.copyfile(package, output / "engine-cli.nupkg")
    write(output / "freeze.json", lock)
    return lock


def study(path):
    path = Path(path)
    lock = load(path / "freeze.json")
    identity = {k: v for k, v in lock.items() if k != "studySha256"}
    require(sha(encoded(identity)) == lock["studySha256"], "Study manifest changed.")
    for name, key in (("candidates.json", "candidatesSha256"), ("protocol.json", "protocolSha256"),
                      ("ANNOTATION.md", "instructionsSha256"), ("engine-cli.nupkg", "cliPackageSha256")):
        require(sha((path / name).read_bytes()) == lock[key], "Frozen study content changed.")
    return lock, load(path / "candidates.json"), load(path / "protocol.json")


def packet(study_path, output, reviewer):
    lock, data, _ = study(study_path)
    require(identifier(reviewer) and reviewer != data["provenance"]["authorId"], "Reviewer must differ from candidate author.")
    output = Path(output)
    output.mkdir(parents=True, exist_ok=False)
    # Intent, categories, expected negatives, other annotations and engine artifacts stay out.
    docs = [{k: d[k] for k in ("documentId", "source", "language", "recordedOn", "text", "roster")}
            for d in data["documents"]]
    write(output / "documents.json", {"studySha256": lock["studySha256"], "documents": docs})
    write(output / "annotations.json", {"studySha256": lock["studySha256"], "reviewerId": reviewer,
                                        "reviewerType": "model", "blind": True,
                                        "documents": [{"documentId": d["documentId"], "complete": False,
                                                       "spans": [], "notes": ""} for d in docs]})
    shutil.copyfile(Path(study_path) / "ANNOTATION.md", output / "ANNOTATION.md")


def annotation(study_path, input_path):
    lock, data, protocol = study(study_path)
    value = load(input_path)
    require(value["studySha256"] == lock["studySha256"], "Annotation is for a different study.")
    require(identifier(value["reviewerId"]) and value["reviewerId"] != data["provenance"]["authorId"], "Invalid reviewer identity.")
    require(value["reviewerType"] in {"human", "model"} and value["blind"] is True, "Review must declare its type and blindness.")
    originals = {d["documentId"]: d for d in data["documents"]}
    seen = set()
    for doc in value["documents"]:
        doc_id = doc["documentId"]
        require(doc_id in originals and doc_id not in seen, "Unknown or duplicate annotated document.")
        seen.add(doc_id)
        require(doc["complete"] is True, "An incomplete annotation cannot be scored as no identifiers.")
        require(isinstance(doc["spans"], list) and isinstance(doc["notes"], str), "Invalid annotation fields.")
        original = originals[doc_id]["text"].encode("utf-16-le")
        spans = set()
        for span in doc["spans"]:
            require(set(span) == {"start", "length", "kind", "quote"}, "Unexpected span fields.")
            start, length = span["start"], span["length"]
            require(type(start) is int and type(length) is int and start >= 0 and length > 0
                    and 2 * (start + length) <= len(original), "Span lies outside UTF-16 text.")
            try:
                quote = original[2 * start:2 * (start + length)].decode("utf-16-le")
            except UnicodeError:
                raise ValueError("Span splits a Unicode character.") from None
            require(quote == span["quote"] and span["kind"] in protocol["kinds"], "Span quote or kind is invalid.")
            key = (start, length, span["kind"])
            require(key not in spans, "Duplicate annotated span.")
            spans.add(key)
    require(seen == set(originals), "Every document needs an explicit completed review, including negatives.")
    return value


def compare(study_path, a_path, b_path):
    a, b = annotation(study_path, a_path), annotation(study_path, b_path)
    require(a["reviewerId"] != b["reviewerId"], "Two annotations require different reviewers.")
    right = {d["documentId"]: d for d in b["documents"]}
    counts = []
    for left in a["documents"]:
        keys = lambda d: {(s["start"], s["length"], s["kind"]) for s in d["spans"]}
        aa, bb = keys(left), keys(right[left["documentId"]])
        counts.append({"documentId": left["documentId"], "agreedSpans": len(aa & bb),
                       "onlyA": len(aa - bb), "onlyB": len(bb - aa), "requiresAdjudication": aa != bb})
    return {"stage": "pre-adjudication-not-gold", "studySha256": a["studySha256"],
            "reviews": [{"id": v["reviewerId"], "type": v["reviewerType"], "sha256": sha(Path(p).read_bytes())}
                        for v, p in ((a, a_path), (b, b_path))],
            "documents": counts, "disagreements": sum(d["requiresAdjudication"] for d in counts),
            "caveat": "Reviewer identities and blindness are declarations, not authenticated independence. Agreement is not accuracy."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    freeze_parser = commands.add_parser("freeze")
    freeze_parser.add_argument("--input", required=True, type=Path)
    freeze_parser.add_argument("--output", required=True, type=Path)
    freeze_parser.add_argument("--cli-package", required=True, type=Path)
    freeze_parser.add_argument("--engine-commit", required=True)
    packet_parser = commands.add_parser("packet")
    packet_parser.add_argument("--study", required=True, type=Path)
    packet_parser.add_argument("--output", required=True, type=Path)
    packet_parser.add_argument("--reviewer", required=True)
    check_parser = commands.add_parser("check")
    check_parser.add_argument("--study", required=True, type=Path)
    check_parser.add_argument("--annotation", required=True, type=Path)
    compare_parser = commands.add_parser("compare")
    compare_parser.add_argument("--study", required=True, type=Path)
    compare_parser.add_argument("--a", required=True, type=Path)
    compare_parser.add_argument("--b", required=True, type=Path)
    compare_parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.command == "freeze":
        result = freeze(args.input, args.output, args.cli_package, args.engine_commit)
        print("Frozen study: " + result["studySha256"])
    elif args.command == "packet":
        packet(args.study, args.output, args.reviewer)
        print("Blind packet ready; no annotations have been completed.")
    elif args.command == "check":
        annotation(args.study, args.annotation)
        print("Complete annotation is structurally valid; this is not an engine evaluation.")
    else:
        result = compare(args.study, args.a, args.b)
        with args.output.open("xb") as stream:
            stream.write(encoded(result))
        print(f"Documents requiring adjudication: {result['disagreements']}")


if __name__ == "__main__":
    main()
