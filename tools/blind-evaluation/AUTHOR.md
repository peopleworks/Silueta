# Candidate author instructions

Work in a fresh session that has not helped implement Silueta. Read only this file and `protocol.json`.
Do not inspect source code, existing corpora, tests, detector thresholds or engine outputs. Do not run
Silueta. If you have seen those materials, disclose it and let the coordinator choose another author.
The coordinator wrote the protocol, so author independence remains limited by that design. A model's
training-data independence is unknown and must not be claimed.

Create `candidates.json` with 24 **new, fully synthetic, manually written** home-care transcripts:
four per scenario, two English and two Spanish within each scenario. All people and identifying
details are invented. Do not reuse examples from this project. Use reserved domains such as
`example.com` and obviously fictional contact details. `source` is `synthetic-manual`: handwritten
spelling damage is not output from a speech recognizer. Each transcript should be 80–180 words.

Scenarios:

1. `common-words`: names/places that also resemble ordinary vocabulary, with clinical terms and
   non-identifying controls. Include two complete negative transcripts here (one per language).
2. `aliases`: nicknames, first/last name mentions, accent variation and manually simulated spelling
   damage. Mix easy and hard mentions; do not assume any particular matcher behavior.
3. `roles`: titles, family/staff roles, ambiguous first names and incidental people absent from the
   roster. Include identifying names after relationships and in ordinary narrative.
4. `continuity`: at least one pair of records per language sharing canonical people/subject ids, with
   repeated mentions and different staff on different visits. Related records share `caseGroup`.
5. `patterns`: varied phones/emails/addresses/dates/record identifiers, ages on both sides of 90,
   and birth-year boundaries relative to `recordedOn`. Include two full negative transcripts here
   (one per language), with only non-identifying clinical numbers and allowed years/ages.
6. `roster-gaps`: two empty-roster documents and two incomplete-roster documents, with people/places
   outside the available context. Missing context does not make identifying content harmless.

Thus four documents are deliberate negatives, one English/one Spanish in each of the named groups.
Do not flag their status per document or supply expected spans/results. That intent stays out of
reviewer packets. A coordinator checks substantive coverage before freezing, without running Silueta.

Envelope:

```json
{
  "studyId": "assisted-redaction-pilot-1",
  "provenance": {
    "authorId": "author-b",
    "authorType": "model",
    "method": "Actual model/version and writing method",
    "synthetic": true,
    "consultedImplementation": false,
    "consultedEngineOutput": false
  },
  "documents": []
}
```

Every document has exactly these fields:

```json
{
  "documentId": "pilot-en-01",
  "caseGroup": "family-01",
  "scenario": "common-words",
  "source": "synthetic-manual",
  "language": "en",
  "recordedOn": "2026-10-02",
  "text": "Your new synthetic transcript",
  "roster": [{"value": "Invented Canonical Name", "kind": "PatientName", "subjectId": "person-01"}]
}
```

Use lowercase opaque ids with letters, digits and hyphens (maximum 64 characters, starting with a
letter). Never use a person's name as an id. Roster kinds must be from `protocol.json`; roles are
PatientName, FamilyName, StaffName or OtherName for people. The same subject id must always keep the
same canonical value and case group, including if that person's role changes between records.
Use different ids for different people. Preserve identifiers absent from the roster in the text.

Do not add annotations, answer keys, detector predictions or per-document negative flags. Do not edit
repository files. Write only `candidates.json` to the directory the coordinator gives you, then return
its path and your author identity/method. No evaluation takes place until separate blind annotations
and adjudication are complete.
