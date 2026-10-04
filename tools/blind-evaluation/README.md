# Blind evaluation preparation

Selected use: **assisted redaction with human review before sharing**. This workflow prepares a new
24-document synthetic challenge set. It does not certify accuracy, represent real calls, choose a
release threshold, or complete an independent human evaluation. Candidate authors, reviewers and
adjudicators have separate roles. A new model/session provides another perspective; its independence
from training data cannot be established here.

`protocol.json` freezes six scenarios, four documents each, balanced English/Spanish, a fixed record
date and no numeric release criterion. [`AUTHOR.md`](AUTHOR.md) specifies new candidates and four
negative controls. [`ANNOTATION.md`](ANNOTATION.md) defines target labels, contextual ambiguity and
UTF-16 offsets. Read these before collecting data. The protocol is designed by the project team,
so even an external author does not make the entire study independent of that design.

## Sequence

1. Give a fresh external author only `AUTHOR.md` and `protocol.json`. Candidates remain private under
   `AI_Tasks/` or `artifacts/`. Check coverage/provenance without running the engine. Fix substantive
   omissions now. Structural validation cannot prove that a narrative contains its intended cases.
2. Build/pack the selected engine revision; record its full SHA and actual CLI nupkg. Freeze candidates,
   protocol, annotation instructions and that package in a new study directory. Never update it in place.
3. Produce two separate reviewer packets. Each reviewer gets only its own packet and no source,
   author intent, other annotations or engine results. Use fresh sessions; the author cannot annotate
   its own candidates. The tooling checks declarations and hashes, not a reviewer's identity/access.
4. Require explicit completion for every document, including negatives. Validate offsets/quotes and
   compare independent reviews. Preserve both raw reviews. Do not score their union or treat agreement
   as correct labels. A human adjudicator resolves disagreements against the original before results
   are viewed, recording decisions/reasons and actual human involvement.
5. Only then export one adjudicated gold set for `silueta evaluate`. Keep adjudication and reviewer
   metadata beside it. The existing loader infers annotators from spans and does not interpret the
   `annotation` metadata; its annotator count therefore does not measure completed negative reviews
   or human adjudication. Do not relabel a model pass as human review to populate that count.
6. Run the frozen engine on the adjudicated gold, then conduct the assisted-workflow pilot with a
   human operator who sees input, roster and redacted output but not gold labels. Preserve initial
   output, corrections and final output. An assessor compares both outputs against frozen gold.

The study stays blind only until its first engine results are inspected. If it informs detector fixes,
it becomes development/regression evidence; reserve a new untouched set for the next release decision.

## Commands

Python 3.9+, standard library only. These commands never invoke Silueta and refuse existing study/
packet directories. The coordinator supplies concrete private paths when the author is ready:

```powershell
python tools/blind-evaluation/workflow.py freeze --input <candidates.json> --output <new-study-dir> --cli-package <Silueta.Cli.nupkg> --engine-commit <full-sha>
python tools/blind-evaluation/workflow.py packet --study <study-dir> --output <new-review-a-dir> --reviewer review-a
python tools/blind-evaluation/workflow.py packet --study <study-dir> --output <new-review-b-dir> --reviewer review-b
python tools/blind-evaluation/workflow.py check --study <study-dir> --annotation <annotations-a.json>
python tools/blind-evaluation/workflow.py compare --study <study-dir> --a <annotations-a.json> --b <annotations-b.json> --output <new-comparison.json>
python -B -m unittest discover -s tools/blind-evaluation -p "test_*.py"
```

Hashes detect changes relative to the retained manifest; they are not digital signatures or protection
against someone replacing both the manifest and its content. Retain the original study hash separately.
The engine commit is coordinator-supplied provenance; the recorded package/assembly hashes identify
the actual frozen binaries. Do not infer commit identity solely from a reused package version.
Only synthetic data enters this workflow/repository. It has no import path for real transcripts.

## Measurements and the next decision

Report before-review document leaks (all target kinds and in scope separately), character recall and
over-redaction, per language/scenario/kind, with numerator/denominator and negative-control results.
Preserve failures and unresolved label disputes. Report ambiguity/annotation disagreement separately;
there is no engine accuracy score before adjudication. Review burden includes elapsed review time,
number of changed spans, operator omissions and identifying content left in the **final** artifact.
Check subject-code/name continuity and attribution across related records using the same vault;
this is a separate run-level audit, not a metric the existing `evaluate` JSON already supplies.

The existing evaluator reports Wilson intervals and paired document bootstrap comparisons. This
small, purposively selected stress suite is not a probability sample. Related records share case
groups and must not be split across development/holdout or treated as independent observations in
an inferential analysis. Raw document intervals must not be presented as population accuracy or
as a grouped uncertainty estimate; grouped analysis and an independently sampled pilot are later work.

Acceptance criteria were fixed on 3 October 2026, before any engine output on the candidates existed:
see [`ACCEPTANCE.md`](ACCEPTANCE.md). They gate on three things and set no leak-rate threshold. Human
review must be complete before sharing; internal zero residue is not a guarantee of zero leaks. Synthetic
stress results alone do not authorize the stable package: a release candidate must also pass a period of
real use. [NIST's evaluation guidance](https://airc.nist.gov/airmf-resources/playbook/measure/) provides
background on documenting test sets/methods and involving assessors outside front-line development.
