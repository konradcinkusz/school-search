# Ranking methodology

> **DRAFT, methodology version `0.0-draft`.** Not in use. No score is published in Phase A.
> The API returns `methodologyVersion: "0.0-draft"` and `dataVintage: null` until Phase B.
> This document is marked DRAFT until Phase B's scoring is reviewed.

## 1. Purpose and anti-goals

The methodology estimates how well a primary school's exam results compare with the national picture, for a
given cohort, with an honest statement of confidence. It is a descriptive proxy, not an official ranking.

Anti-goals, enforced by the architecture:

- **No per-person view.** The unit of evaluation is the school and its cohort. There is no teacher or pupil entity.
- **No ascending sort on a composite score.** Lists are never ordered so that the weakest school comes first by default.
- **No score below the cohort threshold** (ADR-0009). Below threshold the response is `insufficient_cohort`, not null or 0.
- **Scoring references no billing, entitlement or user types** (architecture test).
- **No nationality or foreign-pupil data** in any input.
- **No inferred heuristics about people** in any component.

## 2. Inputs

| Input | Source | Status |
|---|---|---|
| Exam results per school, subject and year | CKE per-school export | OPEN: source, join key and licence (`SOURCES.md` §3.3) |
| Cohort size: class VIII pupils who sat each exam | CKE sat counts, or SIO class VIII | OPEN (`SOURCES.md` §3.6) |
| School identity and type | SIO register | Per brief (`SOURCES.md` §1) |
| National mean per subject per year | CKE national figures | OPEN: confirm publication and licence |

## 3. Exam component

- Standardised per **subject** and per **year** against the **national mean** for that subject and year.
- Three subjects with **equal weights**: Polski, matematyka, język obcy.
- The subject mapping, the standardisation formula and the handling of missing subjects are **to be specified in
  Phase B** and recorded here. This draft does not state a formula.

## 4. Counter-metric: cohort coverage

Cohort coverage is the share of the school's class VIII cohort that sat the exams (CKE sat count divided by SIO
class VIII cohort).

- It is **blended into the exam component**, not shown beside it. A school whose average rises while its coverage
  falls must see its score fall, not rise. This is the degenerate strategy it exists to catch: excluding weak pupils
  from the exam raises the average.
- The unit test for this (brief): the score falls when coverage drops while the average rises. Phase B adds it.
- The blend weights are **to be set in Phase B**. They are not chosen in this draft.

## 5. Cohort threshold and suppression

| Class VIII cohort | Output |
|---|---|
| 9 or more | Score, with cohort size, interval, confidence and `methodologyVersion` |
| 5 to 8 | `limited-confidence profile`: descriptive data and sample size; no score; no rank |
| Below 5 | `insufficient_cohort` with cohort size; no value |

Threshold values and the reasoning are in ADR-0009 (Proposed). The threshold lives in one place in the `Scoring`
project.

## 6. Confidence

Every score carries a confidence or sample-size measure in the same object. The reading guide (what "low confidence"
means, in plain Polish, with an example) is part of the public methodology page. The confidence formula is
**to be specified in Phase B**.

## 7. Output contract

Every score is returned in one object containing:

- `score` (number), `interval` (low, high), `confidence`
- `cohortSize`
- `methodologyVersion`
- `source`, `sourceUrl`, `asOf`, `method` (the provenance rule)

A value is never returned without these. In MCP responses the same fields are returned, and user-generated content is
labelled `userGenerated` and kept separate (Phase E).

## 8. Versioning and change control

- Any change to inputs, weights, thresholds or formulas is a new `methodologyVersion`.
- Old versions stay readable, so a score can be reproduced.
- A change is recorded as an ADR before it ships.

## 9. Open items before Phase B sign-off

1. Subject standardisation formula and missing-subject rule (§3).
2. Blend weights for coverage (§4) and the unit test values.
3. Confidence formula (§6).
4. Reconciliation of the cohort counts with the Phase B gate (ADR-0009).
5. Sources and licences for every input (`SOURCES.md`).
