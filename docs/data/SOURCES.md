# Data sources

Every fact here is either **verified** (with how it was checked), **per brief** (stated in the product brief
and not yet checked against the file or site), or **OPEN** (unknown, with how to verify it). Nothing is
guessed. A value shown in the product carries its own `source`, `sourceUrl`, `asOf` and `method`, which
are filled in at ingest, not here.

Last reviewed: 2026-10-11.

## 1. Primary dataset: SIO school register (CC0)

| Item | Status | Value and how it was established |
|---|---|---|
| Dataset | per brief | SIO "Szkoły" dataset, licence CC0 (dataset 288 per brief) |
| File | per brief | `Szkoly_2025.xlsx`, 26 426 rows by 133 columns |
| Primary schools | per brief | 14 224 primary rows; 13 885 real (non-placeholder) population |
| Class VIII cohort, at least 5 pupils | per brief | 12 564 schools |
| Class VIII cohort, at least 9 pupils | per brief | 11 014 schools |
| Source URL | OPEN | Not recorded in the brief. Record the exact download URL and retrieval date at ingest. Verify the licence on the publisher's page at the same time. |
| Row counts | OPEN | The per-brief counts must be reproduced by Phase B's idempotent import before they are used as a gate. |

**Verify at ingest:** run the import twice; the row counts and the class VIII counts above must match on both runs.

### Ownership and privacy fields

- `idTypOrgProw` values 141, 146 and 150 mean a private individual, per brief. **Verified against the official
  dictionary: no** (OPEN, see §3). The brief's mapping is used only to decide that the name is not exposed.
  Store the type. Never expose the name for these values.
- The faith flag is derived from the governing body, never from the school name.

## 2. RSPO (Rejestr Szkół i Placówek Oświatowych) API

| Item | Status |
|---|---|
| Endpoint | `https://api-rspo.mein.gov.pl/api/placowki/?page=1` |
| Probe, 2026-10-11 | **Unreachable from build environment:** `curl: (56) CONNECT tunnel failed, response 403`. The agent proxy reported `connect_rejected` for `api-rspo.mein.gov.pl:443` ("the egress proxy denied the CONNECT (organization policy) or could not reach the destination"). |
| Response fields | OPEN, not observed. Re-probe from an environment that can reach the host. |
| Licence | OPEN |
| Coordinates (lat/long) | OPEN. Not known whether the API returns them. |

**Verify:** from a machine that can reach the host, run
`curl -sS -m 20 -H 'accept: application/json' 'https://api-rspo.mein.gov.pl/api/placowki/?page=1'`, record the
HTTP status, the top-level keys, the licence text on the publisher's page, and whether coordinates exist.
Until then, RSPO is not a dependency of any phase.

## 3. Unknowns (all OPEN)

| # | Unknown | Why it matters | How to verify |
|---|---|---|---|
| 3.1 | RSPO API fields, licence and coordinates (§2) | Could provide coordinates for school distance without a geocoder | Re-probe per §2 from a reachable network; read the publisher's terms |
| 3.2 | `idTypOrgProw` dictionary: full list of codes and meanings | Governs what is exposed. The brief's 141/146/150 mapping is unconfirmed | Obtain the official code list from the SIO or RSPO publisher and record the source URL and date. Do not infer other codes. |
| 3.3 | CKE per-school results export, and its join key to SIO | Needed for every exam component | Locate the CKE publication for the relevant year; check its licence; test the join on a sample and count unmatched rows |
| 3.4 | Stability of `idPodmiot` across SIO releases | Determines whether the school key can be stored across imports | Compare `idPodmiot` for the same school across two releases, when two exist; until then assume it is not stable and key on RSPO if verified |
| 3.5 | Geocoder licence and terms | Blocks address search | See ADR-0010 for the six checks |
| 3.6 | CKE sat-count availability (number of pupils who sat each exam, per school) | Required for cohort size and the coverage counter-metric | Find whether CKE publishes sat counts per school and year, and the licence |
| 3.7 | Catchment (obwód) availability per gmina | Required for the wedge | Check each gmina's published catchment lists for format, licence and coverage. Record coverage as a count of gminas with usable data. Do not assume structured cities are covered. |

## 4. Rules that apply to every source

- Record `source`, `sourceUrl`, `asOf` and `method` for every value that reaches a user.
- Do not fetch or scrape `waszaedukacja.pl` or any other competitor. Do not copy their data or their thresholds
  beyond what the brief records.
- Do not store pupil names from any olympiad or other source. Use them in memory only, and never log or fixture them.
- No nationality or foreign-pupil data is collected, from any source.
- A school may request a correction. The correction path is a Phase C deliverable and is recorded in the UX backlog.
