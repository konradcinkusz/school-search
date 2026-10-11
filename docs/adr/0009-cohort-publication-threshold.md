# ADR-0009: Cohort publication threshold

- **Status:** Proposed (recommended value; owner to confirm before Phase B scoring ships)
- **Date:** 2026-10-11

## Context

Every score carries a cohort size, and a score below a threshold must not be published. The brief fixes the
mechanism (below threshold gives `insufficient_cohort`, never null or 0; the threshold lives in one place) and
records two reference numbers: a CKE floor of **5** and **9** as the figure used by a competitor's published
portal. The brief notes 1 550 schools lie between them.

Figures from the brief, to re-verify against the dataset at ingest (see `docs/data/SOURCES.md`):
- schools with a class VIII cohort of at least 5: **12 564**;
- schools with a class VIII cohort of at least 9: **11 014**;
- difference, the schools in the 5 to 8 band: **1 550** (12 564 − 11 014).

We did not check the competitor's figure from its site. The brief forbids fetching it, so the figure 9 is cited
as recorded in the brief only.

## Decision

1. **Publish a score only at cohort size of at least 9 pupils** in the class VIII cohort (the threshold constant
   `MinCohortForScore = 9`, in one place in `Scoring`).
2. **Between 5 and 8 pupils**, show a `limited-confidence profile`: the descriptive facts (coverage, the raw
   exam averages marked as low-cohort, the sample size) but **no composite score and no rank position**.
3. **Below 5**, show `insufficient_cohort` with the cohort size and no value.

The CKE floor of 5 is therefore the minimum at which anything about exam results is shown; 9 is the minimum at
which a score is computed.

## Options considered

| Option | For | Against |
|---|---|---|
| Score at 5 (CKE floor) | Shows the most schools (about 12 564 with a profile) | A single pupil moves a small cohort's average by a large amount; the score would mostly measure chance |
| **Score at 9, profile at 5 (recommended)** | Scores are stable enough to rank; a middle band keeps information without a rank | About 1 550 schools have no score or rank; more schools show the limited profile |
| Score at 9 and no profile between 5 and 8 | Simplest rule | Hides real data from 1 550 schools, the least defensible outcome for a public portal |
| Higher threshold, e.g. 15 or 20 | Stronger statistics | Removes many more schools; no principled basis in the brief for a higher number |

## Trade-off recorded

Choosing 9 means about **1 550 schools** (12 564 − 11 014 by the brief's figures) show a limited-confidence
profile instead of a score. Those schools are ranked by nobody. A parent searching for a school in a small
village will see descriptive data and an explicit statement of why no score is shown. This is intended. It
costs visibility for those schools and is the price of not presenting noise as a ranking.

A further count is open. The brief's Phase B gate expects about 2 100 schools as `insufficient_cohort`. The
brief's figures do not reconcile to that number by a simple subtraction, so Phase B must produce the
row-level count and this ADR must be updated with it. Until then, treat 2 100 as an expectation, not a fact.

The threshold is a product and ethics judgement (METRIC-ETHICS §3: no number leaves without its confidence),
not a statistical proof. It must be stated in the methodology page with its version.

## Consequences

Threshold changes are a methodology version change. The API returns the threshold and `methodologyVersion` with
every score. Tests assert the boundary values (4, 5, 8, 9) and that no score or rank appears in the 5 to 8 band.
