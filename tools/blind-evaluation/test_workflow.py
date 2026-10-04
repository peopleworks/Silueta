import copy
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

import workflow as w


class BlindWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="silueta-blind-tests-")
        self.root = Path(self.temp.name)
        self.protocol = w.load(w.PROTOCOL)
        self.data = {"studyId": self.protocol["studyId"], "provenance": {
            "authorId": "fixture-author", "authorType": "model", "synthetic": True,
            "consultedImplementation": False, "consultedEngineOutput": False}, "documents": []}
        for scenario in self.protocol["scenarios"]:
            for language in ("en", "es"):
                for index in range(2):
                    key = f"fixture-{scenario}-{language}-{index}"
                    self.data["documents"].append({"documentId": key, "caseGroup": key,
                        "scenario": scenario, "language": language, "source": "synthetic-manual",
                        "recordedOn": self.protocol["recordedOn"], "text": "🩺 Sofia rested. Sofia slept.", "roster": []})
        self.input = self.root / "input.json"
        w.write(self.input, self.data)
        self.package = self.root / "fixture.nupkg"
        with zipfile.ZipFile(self.package, "w") as archive:
            archive.writestr("tools/net10.0/any/Silueta.Core.dll", b"fixture-core-not-a-real-assembly")
            archive.writestr("tools/net10.0/any/silueta.dll", b"fixture-cli-not-a-real-assembly")
        self.study = self.root / "study"
        self.lock = w.freeze(self.input, self.study, self.package, "a" * 40)

    def tearDown(self):
        self.temp.cleanup()

    def review(self, reviewer="review-a", spans=None):
        return {"studySha256": self.lock["studySha256"], "reviewerId": reviewer, "reviewerType": "model",
                "blind": True, "documents": [{"documentId": d["documentId"], "complete": True,
                    "notes": "", "spans": copy.deepcopy(spans or [])} for d in self.data["documents"]]}

    def check(self, value):
        path = self.root / "review.json"
        w.write(path, value)
        return w.annotation(self.study, path)

    def test_freeze_cannot_replace_an_existing_study(self):
        with self.assertRaises(FileExistsError):
            w.freeze(self.input, self.study, self.package, "a" * 40)

    def test_text_tampering_is_rejected_before_annotation(self):
        data = w.load(self.study / "candidates.json")
        data["documents"][0]["text"] += " Altered."
        w.write(self.study / "candidates.json", data)
        with self.assertRaisesRegex(ValueError, "Frozen study content changed"):
            self.check(self.review())

    def test_packet_hides_intent_answers_and_engine(self):
        output = self.root / "packet"
        w.packet(self.study, output, "review-a")
        self.assertEqual({"documents.json", "annotations.json", "ANNOTATION.md"}, {p.name for p in output.iterdir()})
        doc = w.load(output / "documents.json")["documents"][0]
        self.assertNotIn("scenario", doc)
        self.assertNotIn("caseGroup", doc)
        self.assertNotIn("spans", doc)
        with self.assertRaisesRegex(ValueError, "incomplete"):
            w.annotation(self.study, output / "annotations.json")

    def test_candidates_cannot_contain_answer_keys(self):
        self.data["documents"][0]["spans"] = []
        with self.assertRaisesRegex(ValueError, "Unexpected document"):
            w.candidates(self.data, self.protocol)

    def test_author_cannot_be_its_own_blind_reviewer(self):
        with self.assertRaisesRegex(ValueError, "differ"):
            w.packet(self.study, self.root / "packet", "fixture-author")

    def test_explicit_completed_negatives_are_accepted(self):
        self.assertEqual(24, len(self.check(self.review())["documents"]))

    def test_missing_document_cannot_be_counted_as_a_negative(self):
        review = self.review()
        review["documents"].pop()
        with self.assertRaisesRegex(ValueError, "Every document"):
            self.check(review)

    def test_utf16_offsets_handle_emoji_and_repeated_mentions(self):
        text = self.data["documents"][0]["text"]
        first, second = text.index("Sofia"), text.rindex("Sofia")
        spans = [{"start": len(text[:p].encode("utf-16-le")) // 2, "length": 5,
                  "kind": "PatientName", "quote": "Sofia"} for p in (first, second)]
        self.check(self.review(spans=spans))
        bad = self.review(spans=[{**spans[0], "start": first}])
        with self.assertRaisesRegex(ValueError, "quote"):
            self.check(bad)

    def test_span_cannot_split_a_surrogate_pair(self):
        with self.assertRaisesRegex(ValueError, "splits a Unicode"):
            self.check(self.review(spans=[{"start": 0, "length": 1, "kind": "Other", "quote": "x"}]))

    def test_duplicate_spans_and_wrong_study_are_rejected(self):
        span = {"start": 3, "length": 5, "kind": "PatientName", "quote": "Sofia"}
        with self.assertRaisesRegex(ValueError, "Duplicate annotated span"):
            self.check(self.review(spans=[span, span]))
        review = self.review()
        review["studySha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "different study"):
            self.check(review)

    def test_disagreements_are_not_merged_into_gold(self):
        a, b = self.root / "a.json", self.root / "b.json"
        w.write(a, self.review("review-a", [{"start": 3, "length": 5, "kind": "PatientName", "quote": "Sofia"}]))
        w.write(b, self.review("review-b"))
        result = w.compare(self.study, a, b)
        self.assertEqual(24, result["disagreements"])
        self.assertEqual("pre-adjudication-not-gold", result["stage"])
        self.assertTrue(all("spans" not in d for d in result["documents"]))
        self.assertNotIn("Sofia", json.dumps(result))
        w.write(b, self.review("review-a"))
        with self.assertRaisesRegex(ValueError, "different reviewers"):
            w.compare(self.study, a, b)

    def test_duplicate_json_fields_are_not_silently_overwritten(self):
        path = self.root / "duplicate.json"
        path.write_text('{"text":"private-marker","text":"other"}')
        with self.assertRaisesRegex(ValueError, "Duplicate JSON") as failure:
            w.load(path)
        self.assertNotIn("private-marker", str(failure.exception))

    # Export: the human adjudication becomes the gold `silueta evaluate` reads, and nothing else does.

    def adjudication(self):
        """Two reviews, the issue file the adjudication packet carries, and a completed human decision file."""
        text = self.data["documents"][0]["text"]
        first, second = (len(text[:p].encode("utf-16-le")) // 2 for p in (text.index("Sofia"), text.rindex("Sofia")))
        self.first = {"start": first, "length": 5, "kind": "PatientName", "quote": "Sofia"}
        self.second = {"start": second, "length": 5, "kind": "PatientName", "quote": "Sofia"}
        self.a, self.b = self.root / "a.json", self.root / "b.json"
        w.write(self.a, self.review("review-a", [self.first, self.second]))
        w.write(self.b, self.review("review-b", [self.first]))
        doc = self.data["documents"][0]["documentId"]
        self.issues = self.root / "expediente.json"
        w.write(self.issues, {"studySha256": self.lock["studySha256"],
            "reviews": [{"id": "review-a", "type": "model", "sha256": w.sha(self.a.read_bytes())},
                        {"id": "review-b", "type": "model", "sha256": w.sha(self.b.read_bytes())}],
            "issues": [{"issueId": "D01", "documentId": doc, "focus": {"start": second, "length": 5, "quote": "Sofia"},
                        "reviewA": self.second, "reviewB": None}]})
        self.decisions = {"stage": "pending-human-adjudication-not-gold", "studySha256": self.lock["studySha256"],
            "reviewerId": "adjudicator-1", "reviewerType": "human", "humanReviewCompleted": True, "blinding": "fixture",
            "issues": [{"issueId": "D01", "documentId": doc, "decision": "A", "finalSpans": None, "reason": "Same person."}],
            "documents": [{"documentId": d["documentId"], "complete": True, "selectedReview": "A", "finalSpans": None,
                           "notes": ""} for d in self.data["documents"]]}

    def export(self, output="exported"):
        path = self.root / "decisions.json"
        w.write(path, self.decisions)
        return w.export(self.study, self.a, self.b, self.issues, path, self.root / output)

    def test_export_writes_gold_the_evaluator_reads_and_a_receipt_without_text(self):
        self.adjudication()
        receipt = self.export()
        gold = sorted((self.root / "exported" / "gold").glob("*.json"))
        self.assertEqual(24, len(gold))
        doc = w.load(gold[0])
        self.assertEqual({"documentId", "source", "language", "speaker", "recordedOn", "text", "roster", "spans",
                          "annotation"}, set(doc))
        self.assertEqual([(self.first["start"], 5, "PatientName", "adjudicator-1"),
                          (self.second["start"], 5, "PatientName", "adjudicator-1")],
                         [(s["start"], s["length"], s["kind"], s["annotator"]) for s in doc["spans"]])
        self.assertTrue(doc["annotation"]["humanReviewed"])
        self.assertEqual(self.protocol["recordedOn"], doc["recordedOn"])
        self.assertEqual(receipt, w.load(self.root / "exported" / "adjudication-receipt.json"))
        self.assertEqual(w.sha(self.a.read_bytes()), receipt["reviews"][0]["sha256"])
        self.assertNotIn("Sofia", json.dumps(receipt))

    def test_export_requires_a_completed_human_adjudication_with_reasons(self):
        for change in (lambda d: d.update(reviewerType="model"),
                       lambda d: d.update(humanReviewCompleted=False),
                       lambda d: d["documents"][3].update(complete=False),
                       lambda d: d["issues"][0].update(reason="  "),
                       lambda d: d["documents"].pop()):
            self.adjudication()
            change(self.decisions)
            with self.assertRaises(ValueError):
                self.export()
            self.assertFalse((self.root / "exported").exists())

    def test_a_case_decision_must_agree_with_the_documents_final_list(self):
        self.adjudication()
        self.decisions["issues"][0]["decision"] = "B"
        with self.assertRaisesRegex(ValueError, "D01"):
            self.export()

    def test_custom_final_spans_are_checked_and_a_state_is_never_gold(self):
        self.adjudication()
        self.decisions["documents"][0].update(selectedReview=None, finalSpans=[{**self.first, "quote": "Sofie"}])
        with self.assertRaisesRegex(ValueError, "quote"):
            self.export()
        self.decisions["documents"][0]["finalSpans"] = [self.first, {**self.second, "kind": "State"}]
        with self.assertRaisesRegex(ValueError, "State"):
            self.export()
        self.decisions["documents"][0].update(selectedReview="A", finalSpans=[self.first])
        with self.assertRaisesRegex(ValueError, "exactly one"):
            self.export()

    def test_export_refuses_reviews_other_than_the_adjudicated_ones(self):
        self.adjudication()
        w.write(self.b, self.review("review-b"))
        with self.assertRaisesRegex(ValueError, "not the reviews"):
            self.export()

    def test_export_never_replaces_existing_gold(self):
        self.adjudication()
        self.export()
        with self.assertRaises(FileExistsError):
            self.export()

    # C3: after the operator's review, does any gold identifier survive in a final artifact?

    def c3(self, text, spans, final, engine=None, minutes=None):
        """One gold document and an operator packet whose final artifact is `final`."""
        gold, packet = self.root / "gold", self.root / "packet"
        gold.mkdir()
        (packet / "final").mkdir(parents=True)
        (packet / "engine-output").mkdir()
        located = []
        for quote, kind in spans:
            start = len(text[:text.index(quote)].encode("utf-16-le")) // 2
            located.append({"start": start, "length": len(quote.encode("utf-16-le")) // 2, "kind": kind, "annotator": "adj"})
        w.write(gold / "doc-1.json", {"documentId": "doc-1", "source": "synthetic-manual", "language": "en",
            "speaker": None, "recordedOn": "2026-10-02", "text": text, "roster": [], "spans": located, "annotation": {}})
        engine = final if engine is None else engine
        (packet / "engine-output" / "doc-1.txt").write_text(engine, encoding="utf-8")
        (packet / "final" / "doc-1.txt").write_text(final, encoding="utf-8")
        w.write(packet / "packet.json", {"coreSha256": "0" * 64,
            "documents": [{"documentId": "doc-1", "engineOutputSha256": w.sha(engine.encode("utf-8")), "residualSpans": 0}]})
        w.write(packet / "times.json", {"operatorId": "operator-1", "documents": [{"documentId": "doc-1", "minutes": minutes}]})
        return gold, packet

    def test_c3_finds_a_name_that_survived_review_whatever_its_case_or_accents(self):
        gold, packet = self.c3("Sofía Reyes called.", [("Sofía Reyes", "PatientName")], "SOFIA REYES called.")
        report = w.assess(gold, packet, self.root / "c3")
        self.assertEqual(1, report["survivals"])
        self.assertEqual("full", report["documents"][0]["survivals"][0]["match"])
        self.assertNotIn("SOFIA", json.dumps(report).upper())
        self.assertIn("SOFIA REYES", (self.root / "c3" / "c3-sheet.md").read_text(encoding="utf-8"))

    def test_c3_reports_a_surname_left_behind(self):
        gold, packet = self.c3("Sofía Reyes called.", [("Sofía Reyes", "PatientName")], "Noa Reyes called.")
        survival = w.assess(gold, packet, self.root / "c3")["documents"][0]["survivals"][0]
        self.assertEqual("partial", survival["match"])

    def test_c3_sees_the_damaged_half_of_a_name_left_where_it_stood(self):
        # From the public corpus: a recogniser turned a first name into "He fell"; the engine replaced only the
        # surname. Ordinary words, lower case, still the person's name in that place.
        gold, packet = self.c3("Nurse He fell Ramirez came.", [("He fell Ramirez", "StaffName")],
                               "Nurse He fell Luca came.")
        survival = w.assess(gold, packet, self.root / "c3")["documents"][0]["survivals"][0]
        self.assertEqual("partial", survival["match"])

    def test_c3_reads_a_number_through_other_separators(self):
        gold, packet = self.c3("Call 602-555-0147 today.", [("602-555-0147", "Phone")], "Call 602 555 0147 today.")
        self.assertEqual(1, w.assess(gold, packet, self.root / "c3")["survivals"])

    def test_c3_does_not_flag_a_name_inside_another_word(self):
        gold, packet = self.c3("Rose brought roses.", [("Rose", "FamilyName")], "Noa brought roses.")
        self.assertEqual(0, w.assess(gold, packet, self.root / "c3")["survivals"])

    def test_c3_counts_what_the_operator_changed_and_the_minutes_it_took(self):
        gold, packet = self.c3("Sofía called 602-555-0147.", [("Sofía", "PatientName"), ("602-555-0147", "Phone")],
                               "Noa called [PHONE].", engine="Noa called 602-555-0147.", minutes=2)
        report = w.assess(gold, packet, self.root / "c3")
        self.assertEqual((0, 1, 2), (report["survivals"], report["documents"][0]["operatorChangedWords"],
                                     report["documents"][0]["minutes"]))

    def test_c3_needs_every_final_artifact_and_an_untouched_engine_output(self):
        gold, packet = self.c3("Sofía called.", [("Sofía", "PatientName")], "Noa called.")
        (packet / "final" / "doc-1.txt").unlink()
        with self.assertRaisesRegex(ValueError, "final artifact"):
            w.assess(gold, packet, self.root / "c3")
        (packet / "final" / "doc-1.txt").write_text("Noa called.", encoding="utf-8")
        (packet / "engine-output" / "doc-1.txt").write_text("Edited.", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "engine output"):
            w.assess(gold, packet, self.root / "c3")
        self.assertFalse((self.root / "c3").exists())

    def test_subjects_cannot_cross_case_groups_or_change_identity(self):
        for value in ("Fixture Name", "Different Name"):
            data = copy.deepcopy(self.data)
            data["documents"][0]["roster"] = [{"value": "Fixture Name", "kind": "PatientName", "subjectId": "p-1"}]
            data["documents"][1]["roster"] = [{"value": value, "kind": "PatientName", "subjectId": "p-1"}]
            with self.assertRaisesRegex(ValueError, "changed identity or crossed"):
                w.candidates(data, self.protocol)


if __name__ == "__main__":
    unittest.main()
