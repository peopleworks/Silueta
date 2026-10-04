# Acceptance criteria for 1.0

Fixed on **3 October 2026** by the maintainer, **before any engine output on the pilot candidates was
produced or inspected**. This file is committed so its date can be checked against that of the results.
It does not change after results exist: a different decision needs a new, untouched set.

| Pilot | |
| --- | --- |
| Study | `assisted-redaction-pilot-1`, study SHA-256 `79c1233b6e8504147d3a8596bc2c347c7c01c17fca118dccdad1231661cbff16` |
| Engine | commit `e532d4ca1496047a99ddfbd0ddbe95fb9b486dfa`, frozen CLI package; Core assembly SHA-256 `add019085210a6847024f27c676d9759e35d0f3355438f2a1ea09ae61f8b80af` |
| Scope | assisted redaction with human review before sharing |

The scope is the claim 1.0 makes. Silueta 1.0 does not claim that its output can be shared without a person
reviewing it, and nothing below would support that claim.

## Roles

- **Adjudicator** (the maintainer): resolves the reviewers' disagreements and confirms having read all 24
  documents, recording a reason per decision, before any engine output exists.
- **Operator** (a different person): sees each input, its roster and the redacted output, never the gold
  labels, and corrects the output as they would before sharing it.
- **Assessor**: compares outputs against the frozen gold. Knowing the gold does not bias that comparison, so
  the adjudicator may assess.

## The three gates

All three must pass. They are computed on the adjudicated gold with the frozen engine above.

**C1. The matcher is worth more than a list.** In scope, `silueta` has a higher recall than `deny-list`,
the literal roster baseline, both as reported by `silueta evaluate` over the adjudicated gold. The point
estimate decides. The paired bootstrap interval is published as it comes out.

**C2. Nobody is given somebody else's name.** Run as `silueta evaluate` runs: one vault across the corpus, in
the evaluator's document order.
- *Continuity, checked automatically:* every subject keeps a single invented name across all of its
  records, and no invented name is retired during the run.
- *Attribution, confirmed by the assessor:* every replacement the engine attributed to a subject is listed
  with the gold spans it overlaps. Mechanical flags mark a kind that differs from the subject's roster
  kind, a replacement that overlaps no gold span, and one that overlaps several. The flags direct
  attention; they are not failures, because an annotator's role label can legitimately differ from the
  roster's. The listing is complete, not sampled. C2 passes when continuity holds and the assessor confirms
  that no replacement put one person's invented name on a mention of another person.

A rule that decided from the text alone which person a span names would be a second matcher, disagreeing
with the first exactly where it matters. That judgement is therefore a person's.

**C3. Nothing identifying is left after review.** After the operator's review, no identifier in the
adjudicated gold survives in the final artifacts of any of the 24 documents, the four negative controls
included.

## Measured and published, not gated

Before-review document leak rate (all target kinds and in scope), recall and over-redaction, by language and
scenario, with numerators and denominators; the negative controls; and the review burden: elapsed time,
spans the operator changed, and the operator's omissions. Label disputes the adjudication could not settle
are reported, not dropped.

## When a gate fails

The failure is analysed and published. A fix informed by this pilot makes the pilot development data, and the
next decision needs a new, untouched set (pilot-2). The same set is never re-run until it passes.

## After the gates

`1.0.0-rc.1` is published and integrated from nuget.org into one of the maintainer's own projects for two to
three weeks of real use. If no public API or stored format has to change in that time, the same content is
released as `1.0.0`. A change restarts the period.

## Tooling still to write before the engine runs

- ~~export of the adjudicated labels to the `GoldCorpus` format `silueta evaluate` reads~~: `workflow.py export`,
  added after this file was committed;
- ~~the C2 audit~~: [`tools/Silueta.ContinuityAudit`](../Silueta.ContinuityAudit/README.md), added after
  this file was committed;
- ~~the operator packet: redacted outputs made with the shared vault, without gold~~: `--operator-packet` of the
  C2 audit, added after this file was committed;
- ~~the C3 assessment of the operator's final artifacts against the gold~~: `workflow.py assess`, added after
  this file was committed.
