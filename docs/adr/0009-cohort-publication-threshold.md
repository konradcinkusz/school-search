# ADR-0009: Cohort publication threshold

- **Status:** Accepted (9 for a score, 5 as the floor for any exam figure). The 5 to 8 limited profile is built (`scoreStatus` `limited`, no score, no rank; backlog A-11, not yet compiled in this environment); below 5 stays `insufficient_cohort`
- **Date:** 2026-10-11
- **Deciders:** owner (Konrad Cinkusz), to confirm at Phase B review. The one wording point under "Open points"
  must be confirmed before the first public score.

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
as recorded in the brief only. The threshold of 9 is therefore a product and ethics judgement, with the brief as
its only source, and it is not a statistical result.

## Decision

1. **Publish a score only at a cohort of at least 9 pupils who sat the exam.** The constant is
   `ScoringPolicy.PublicationThreshold = 9`, in one place in `SchoolSearch.Api/Scoring`. At 9 and above the engine
   publishes the score, interval and rank.
2. **Between 5 and 8 pupils**, the output is the `limited-confidence profile` (`scoreStatus` `limited`): the
   descriptive data (the raw exam averages per subject, each with its pupil count, marked as low-cohort) and the
   cohort size, but **no composite score, no interval and no rank position**. The engine produces it; the web shows
   it with the line "Dla tej grupy nie publikujemy wyniku ani miejsca w zestawieniu." The floor is
   `ScoringPolicy.LimitedBandFloor = 5`. *Status: wording updated to match the implementation; version bump / ADR
   amendment pending owner decision.*
3. **Below 5**, or with a class VIII headcount of 0 or less, show `insufficient_cohort` with the cohort size and no
   value.

The CKE floor of 5 is the minimum at which anything about exam results is intended to be shown; 9 is the minimum
at which a score is computed.

## Reasoning for 9 and 5

- A small cohort's average moves a lot with one pupil. Below about nine, the score would mostly measure chance,
  and the interval would be too wide to rank by. A ranking of noise is the outcome the product must avoid.
- Holding back all data between 5 and 8 would hide real, descriptive information from about 1 550 schools
  (by the brief's figures). That is the least defensible outcome for a public portal. The 5 floor keeps the
  descriptive band available for Phase C.
- A higher threshold (15 or 20) has no basis in the brief and would remove many more schools.

## Options considered

| Option | For | Against |
|---|---|---|
| Score at 5 (CKE floor) | Shows the most schools (about 12 564 with a profile) | A single pupil moves a small cohort's average by a large amount; the score would mostly measure chance |
| **Score at 9, profile at 5 (chosen)** | Scores are stable enough to rank; a middle band keeps information without a rank | About 1 550 schools have no score or rank; they show the limited profile, which is built but whose tests have not yet run in CI |
| Score at 9 and no profile between 5 and 8 | Simplest rule | Hides real data from 1 550 schools, the least defensible outcome for a public portal |
| Higher threshold, e.g. 15 or 20 | Stronger statistics | Removes many more schools; no principled basis in the brief for a higher number |

## Trade-off recorded

Choosing 9 means about **1 550 schools** (12 564 − 11 014 by the brief's figures) do not get a score or a rank.
A parent searching for a school in a small village will see an explicit statement of why no score is shown. This
is intended. It costs visibility for those schools and is the price of not presenting noise as a ranking.

## Open points

1. **Count of `insufficient_cohort` schools is not reconciled.** The brief's Phase B gate expects about 2 100
   schools. The brief's figures do not reconcile to that number. The brief does not state the base of its
   counts (14 224 primary rows, or 13 885 real schools), so no single subtraction gives it. Arithmetic on the brief's
   figures alone gives 3 210 (14 224 − 11 014) or 2 871 (13 885 − 11 014) below 9, depending on the base. These
   are not verified counts. **No count is claimed here.** The row-level count from the idempotent import replaces
   all of them. Until then, 2 100 is an expectation, not a fact. The brief's figures count schools below 9. Under the
   current bands, `insufficient_cohort` means below 5 and `limited` means 5 to 8, so the row-level count must be
   split the same way before it is compared with 2 100.
2. **Basis of the threshold: pupils who sat, not the class VIII headcount.** The first draft of this decision said
   "in the class VIII cohort". The Phase B contract applies the threshold to the **pupils who sat the exam**
   (`max PupilsSat`), because a score is computed from them. The two differ whenever coverage is below 1. The
   engine follows the contract; the owner should confirm this basis. If the owner prefers the headcount, the
   threshold basis changes and that is a methodology version change.
3. **The 5 to 8 profile is built, under the unchanged methodology version `1.0.0`.** The engine returns `limited` and
   the web renders it (Decision 2). Whether this needs a version bump to `1.1.0` or an amendment of this ADR is the
   owner decision at the end of this record. Until it is decided, the methodology text is worded to the
   implementation, with a visible status note.
4. **The explain endpoint still answers 200 for limited and insufficient schools (the web hides the panel); a
   backend refusal is an owner decision.** `POST /api/v1/schools/{slug}/explain` (`ExplainService`) has no
   `scoreStatus` gate, so it does not refuse a school with no score. The web does not show the panel for those
   schools. Whether the API should refuse them is open.

## Consequences

- Threshold changes are a methodology version change (`docs/methodology/RANKING-METHODOLOGY.md` §9). The API
  returns `methodologyVersion` with every score, and the methodology page states the threshold.
- Tests assert the boundary values (4, 5, 8, 9): 9 is published, 5 to 8 is `limited` with no score, no interval and
  no rank, and 4 and below are `insufficient_cohort`. No score or rank appears for a suppressed school.
- The ranking builder ranks only non-null scores, so a suppressed school, limited or insufficient, is never ranked
  (`RankingBuilder`).
- The 5 to 8 profile is built. Whether it needs a methodology version bump or an ADR amendment is the open decision
  below.

## Open decision for the owner (pending)

- **Does the 5 to 8 `limited` band need a methodology version bump (`1.0.0` to `1.1.0`) or an amendment of this
  ADR?** The band is implemented in the engine and the web while `MethodologyVersion` is still `1.0.0`. The
  methodology wording has been changed to match the code, with a status note. **Not decided. No version has been
  bumped by this change.**
