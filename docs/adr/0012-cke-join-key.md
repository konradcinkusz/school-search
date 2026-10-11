# ADR-0012: CKE join key

- **Status:** Pending (decision not yet taken for the real export; the matching rule is fixed now, the key column is not verified)
- **Date:** 2026-10-11
- **Deciders:** owner, at Phase B review

## Context

The exam component (`docs/methodology/RANKING-METHODOLOGY.md` §3) needs each school's CKE results joined to its
SIO record. The CKE per-school export is OPEN: its format, its join key and its licence are not known
(`docs/data/SOURCES.md` §3.3). The SIO key `idPodmiot` may not be stable across releases (§3.4). RSPO fields are
OPEN (§2) and the RSPO host was unreachable from the build environment on 2026-10-11.

A fuzzy match on school name or address would look like it works and would silently attach results to the wrong
school. A wrong attachment is worse than a missing one, because it publishes a score against the wrong name.

The Phase B import (`CkeLoader`) is written against a column map. Its join-key column defaults to `rspo`, which is
UNVERIFIED. This ADR fixes the matching rule, so the code cannot drift to fuzzy matching while the key is unknown.

## Decision

1. **Exact join only.** A CKE row is attached to a school only when its key equals a school's key exactly
   (after trimming whitespace), on `idPodmiot` or on the RSPO identifier, as named by the column map. No other key.
2. **Unmatched rows are rejected and reported.** A row with no exact match is not written. It is counted in
   `RowsRejected`, and its reason is listed in `ImportReport.RejectionReasons`. Nothing is guessed.
3. **Fuzzy matching is explicitly rejected.** No matching on school name, address, gmina, normalised text, edit
   distance or any similarity score. Any such matching requires a later ADR, with its error rate measured on a
   verified sample before it is used.
4. **Missing key column aborts the run.** If the mapped key column is absent from the file, the loader fails fast
   and writes nothing (exit code 1), listing the missing header.
5. **Idempotent.** Re-running the same file produces the same rows, keyed by `(SchoolId, ExamYear, Subject)`.

## Options considered

| Option | For | Against |
|---|---|---|
| **Exact key on idPodmiot or RSPO, reject the rest (chosen)** | Attaches only what is certainly the same school; errors are visible and counted | Some rows will be rejected; the rejected share is unknown until the export is read |
| Fuzzy match on name and gmina | Matches more rows | Names repeat across gminas and change over time; silent wrong attachments; no measured error rate |
| Fuzzy match with manual review | Higher recall | Needs a review workflow that does not exist, and a reviewer's decisions would need an audit trail |
| Use CKE's own school code, if one exists | Likely stable | Unknown until the export is read; would need its own ADR amendment |

## Consequences

- Until the export is read, the default key `rspo` in `CkeColumnMap` is **UNVERIFIED**. The owner must confirm which
  column exists and what it contains before the first import.
- The rejected share is a reported number from each run, not an assumption. It goes in the import report and the
  run record (`ImportRuns.RowsRejected`). A high rejected share is a reason to stop and check the key, not to add
  fuzzy matching.
- If `idPodmiot` changes across SIO releases (`SOURCES.md` §3.4), an exact join on it will reject rows after the
  change. The fix is a verified key mapping recorded in a new ADR, not a fuzzy match.

## How to verify when the export becomes reachable

Run these from a machine that can reach the CKE and SIO publications. Record each result with its date and URL in
`docs/data/SOURCES.md`. Do not record a count that has not been produced by a run.

1. Download the CKE per-school export for the exam year. Record the exact URL, the retrieval date and the licence
   text from the publisher's page (`SOURCES.md` §3.3).
2. Read the header row. Record every column name exactly. Confirm whether a column holds `idPodmiot`, RSPO or
   neither.
3. Take a sample of 20 to 50 CKE rows. For each, confirm by hand, against the school's own published record, that the
   key points to the same school. Record the sample size and the number confirmed.
4. Run the importer on the file with the verified column map (`cke --file F --year Y`). Read the reported
   `RowsRead`, `RowsWritten` and `RowsRejected`, and the first rejection reasons.
5. Run it a second time on the same file. Row counts and values must be identical.
6. If the rejected share is high, stop. Record the cause in this ADR, and do not add a matching rule without a new ADR.
7. Once the key is verified, change this status to Accepted and record the verified column name and the sample result.
