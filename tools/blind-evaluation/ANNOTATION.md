# Blind annotation instructions

This packet contains synthetic home-care transcripts. The study concerns **assisted redaction with
human review before sharing**. Do not read implementation, tests, old corpora, results, or another
reviewer's annotations. Do not run Silueta. Only read this packet. Tell the coordinator if you already
saw any of those materials; do not claim a blind review afterwards.

The task is to mark what needs removing, regardless of whether a detector is likely to find it.
The roster gives canonical names and opaque subject ids. It does not exhaust the identifiers in the
text. Missing-roster names still count. A manually damaged spelling still identifies its person when
the narrative/roster supports that reading. When ambiguous, mark your interpretation and explain it
in notes; a later adjudicator resolves disagreements before any engine results are viewed.

For this study, remove patient/client, family, staff and incidental person names; their identifying
aliases; telephone numbers, emails, URLs and IP addresses; addresses, cities and full postal codes;
record/account/device identifiers; date elements other than an isolated year; ages 90 or above and
birth years that imply age 90 or above on `recordedOn`. Remove named organizations and branded
products if present; retain generic medication names and clinical descriptions. These are the study's
explicit target labels, not a claim that every kind can be detected or a legal determination.
Keep a state name alone, a non-identifying isolated year, ordinary vocabulary, generic medicines,
clinical measurements, and ages below 90. Choose `Other` for an identifying item outside the taxonomy
and explain why. Do not silently exclude hard cases or identifiers absent from the roster.

Kinds: `Other`, `PatientName`, `FamilyName`, `StaffName`, `OtherName`, `ClientName`, `Phone`, `Email`,
`Url`, `IpAddress`, `Address`, `PostalCode`, `Date`, `AgeOver89`, `RecordNumber`, `AccountNumber`,
`DeviceId`, `Organization`, `Product`, `City`, `State`. `State` is contextual and normally retained in
this study, so it is not a removal span. Use the person's role when known; do not label a client
company `ClientName`. Prefer specific kinds over `Other`.

Edit only `annotations.json`. Keep the supplied `studySha256` and document ids. Set your real reviewer
type (`model` or `human`); an AI pass is never a human review. Add your model/version or human review
method as an optional top-level `method` string. `blind: true` declares compliance with the instruction;
the tooling cannot authenticate it. Review every document, including those with no identifiers, and
set `complete: true` only after finishing. An empty array with `complete: false` is unfinished work.

Each span has exactly `start`, `length`, `kind`, `quote`. Offsets are **UTF-16 code units**, as in .NET
strings; `quote` must match the original substring exactly. Mark each occurrence separately, without
trailing whitespace or punctuation. Do not normalize accents, line endings or spelling. Python
indexes Unicode code points, so convert a prefix/substring with
`len(value.encode("utf-16-le")) // 2` rather than assuming its `len()` is a .NET offset. Do not split
a surrogate pair. Repeated text requires choosing the correct occurrence, not replacing the first
index everywhere. Notes may explain disputed boundaries, roles or sensitivity; never change the text.

When complete, return only the annotation file path and your declared reviewer identity/type.
Do not generate an engine report, modify detectors, or decide whether a release should pass.
