# Scoring (placeholder)

Phase A: this directory is intentionally empty of code. The scoring engine arrives in Phase B
(ingest and scoring), with the METRIC-ETHICS gate.

Rules that already bind this folder, enforced by the architecture tests (A4) once they exist:

- No reference to billing, entitlement, payment or user types (P-F, monetisation is kept out of scoring).
- Scores are suppressed below the cohort threshold inside the engine (`insufficient_cohort`, never null or 0).
- Every score carries cohort size, interval, `methodologyVersion` and provenance in the same object.
- No teacher, pupil or nationality data is read or produced here.

The methodology is described in `docs/methodology/RANKING-METHODOLOGY.md` (DRAFT until Phase B).
