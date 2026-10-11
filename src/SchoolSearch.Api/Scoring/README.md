# Scoring

Pure scoring engine (`SchoolSearch.Api.Scoring`): school exam results against the national baseline of the
same year, plus the descending ranking of published scores. No I/O, no clock. The method is in
`docs/methodology/RANKING-METHODOLOGY.md` (owned by the docs agent; DRAFT until the Phase B data is verified).

## Invariants

- **Never null, 0 or a guess below the threshold.** Cohort below `ScoringPolicy.PublicationThreshold` (9 pupils
  who sat, ADR-0009) yields `ScoreDto` with a null score and reason `insufficient_cohort`. The threshold lives only in `ScoringPolicy`.
- **Every score carries its cohort, interval and methodology version** in the same `ScoreDto` (METRIC-ETHICS §3).
- **Counter-metric blended, not beside.** Coverage (sat / class VIII headcount) multiplies every subject mean
  below `CoverageFloor` (0.8), so the score cannot rise by lowering coverage (METRIC-ETHICS §2).
- **Equal weights** across the subjects present in both the school input and the national baseline.
- **Data bugs throw.** Year mismatch, duplicate subjects, out-of-range values, zero-pupil subjects and no overlapping
  subject raise exceptions. They are never hidden as suppression.
- **Descending only.** `RankingBuilder` ranks published scores highest first, with competition ranks (1, 1, 3).
  Suppressed schools are listed as `unranked`. There is no ascending or bottom ranking (METRIC-ETHICS §1).
- **Unit of evaluation is the school**, never a person. No pupil, teacher, nationality or inferred-sentiment data is read or produced here (METRIC-ETHICS §4, §5).
- **No billing, entitlement, payment or user types** are referenced from this folder (architecture test A4).

Status: written in Phase B, **not compiled** in this environment (no .NET SDK). CI is the compiler.
