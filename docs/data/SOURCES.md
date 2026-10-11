# Data sources

Every fact here is either **verified** (with how it was checked), **per brief** (stated in the product brief
and not yet checked against the file or site), **UNVERIFIED** (encoded in code as a default, not checked against
the real publication), or **OPEN** (unknown, with how to verify it). Nothing is guessed. A value shown in the
product carries its own `source`, `sourceUrl`, `asOf` and `method`, which are filled in at ingest, not here.

Last reviewed: 2026-10-11 (Phase B).

## 1. Primary dataset: SIO school register (CC0)

| Item | Status | Value and how it was established |
|---|---|---|
| Dataset | per brief | SIO "Szkoły" dataset, licence CC0 (dataset 288 per brief) |
| File | per brief | `Szkoly_2025.xlsx`, 26 426 rows by 133 columns |
| Primary schools | per brief | 14 224 primary rows; 13 885 real (non-placeholder) population |
| Class VIII cohort, at least 5 pupils | per brief | 12 564 schools |
| Class VIII cohort, at least 9 pupils | per brief | 11 014 schools |
| Source URL | OPEN | Not recorded in the brief. Record the exact download URL and retrieval date at ingest. Verify the licence on the publisher's page at the same time. |
| Column headers | UNVERIFIED | The Phase B column map (§5.1) is a default taken from the brief. It has not been checked against the real header row. |
| Row counts | OPEN | The per-brief counts must be reproduced by Phase B's idempotent import before they are used as a gate. No count produced by a run exists yet. |

**Verify at ingest:** run the import twice; the row counts and the class VIII counts above must match on both runs.

### Ownership and privacy fields

- `idTypOrgProw` values 141, 146 and 150 mean a private individual, per brief. **Verified against the official
  dictionary: no** (OPEN, see §3.2). The brief's mapping is used only to decide that the name is not exposed.
  Store the type. Never expose the name for these values. The loader never reads the organ-name column.
- The faith flag is derived from the governing body type 140 only, never from the school name.

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
Until then, RSPO is not a dependency of any phase. The RSPO identifier is one of two exact join keys allowed for
CKE rows (ADR-0012), so it must be verified before any CKE import.

## 3. Unknowns (all OPEN unless stated)

| # | Unknown | Why it matters | How to verify |
|---|---|---|---|
| 3.1 | RSPO API fields, licence and coordinates (§2) | Could provide coordinates for school distance without a geocoder | Re-probe per §2 from a reachable network; read the publisher's terms |
| 3.2 | `idTypOrgProw` dictionary: full list of codes and meanings | Governs what is exposed. The brief's 141/146/150 mapping is unconfirmed. Phase B seeds 14 codes (120, 125, 130, 131, 132, 133, 135, 140, 141, 143, 144, 146, 148, 150) with `LabelVerified = false` and no verified labels. | Obtain the official code list from the SIO or RSPO publisher and record the source URL and date. Do not infer other codes. |
| 3.3 | CKE per-school results export: format, join key and licence | Needed for every exam component. The join key default (`rspo`) is UNVERIFIED. Matching rule fixed in ADR-0012 (pending). | See §5.2 and ADR-0012 "How to verify" |
| 3.4 | Stability of `idPodmiot` across SIO releases | Determines whether the school key can be stored across imports | Compare `idPodmiot` for the same school across two releases, when two exist; until then assume it is not stable and key on RSPO if verified |
| 3.5 | Geocoder licence and terms | Blocks address search | See ADR-0010 for the six checks. No geocoder is invented: coordinates come from a file (`GeoLoader`). |
| 3.6 | CKE sat-count availability (number of pupils who sat each exam, per school) | Required for cohort size and the coverage counter-metric | Find whether CKE publishes sat counts per school and year, and the licence |
| 3.7 | Catchment (obwód) availability per gmina | Required for the wedge | Check each gmina's published catchment lists for format, licence and coverage. Record coverage as a count of gminas with usable data. Do not assume structured cities are covered. |
| 3.8 | Pupil standard deviation per subject and year (`PupilStdDevPoints`) | The interval depends on it, and the engine requires it to be greater than 0. Without it no score is published. | Identify the publisher, licence and exact values. Do not estimate it from other numbers. |
| 3.9 | National mean per subject per year (`NationalMeanPercent`) | The score is measured against it | Confirm the CKE publication, its licence and the exact values. Record them with the URL and date. |

## 4. Rules that apply to every source

- Record `source`, `sourceUrl`, `asOf` and `method` for every value that reaches a user.
- Do not fetch or scrape `waszaedukacja.pl` or any other competitor. Do not copy their data or their thresholds
  beyond what the brief records.
- Do not store pupil names from any olympiad or other source. Use them in memory only, and never log or fixture them.
- No nationality or foreign-pupil data is collected, from any source.
- A school may request a correction. The correction path is a Phase C deliverable and is recorded in the UX backlog.
- Fixtures are synthetic. Test data uses school names such as "Szkoła Podstawowa nr 1 w Testowie" and no person's name.

## 4A. Phase D: preschool sources (przedszkola)

Preschools have no quality score (ADR-0014). Their sources give practical facts only. Each value carries `source`,
`sourceUrl`, `asOf`, `method` and `confidence` (`reported` or `unverified`). Nothing here is fetched by the code: each
source is a file the operator loads with the `sio-preschools` and `preschool-facts` commands.

| Item | Status | Value and how it is established |
|---|---|---|
| SIO preschool units | UNVERIFIED | Same CC0 SIO dataset (288) as §1, filtered to preschool units. The filter value and the column map are not checked against the real file. The default map is the Phase B family (`SioColumnMap`), overridable through the JSON column-map mechanism. Record the URL, the retrieval date and the header row at first load. |
| SIO preschool headers | UNVERIFIED | Not read from a real file. Check the header row before the first load, and record each difference from the default map. |
| Gmina vacancy lists (free places) | OPEN | Each gmina (kuratorium or gmina office) publishes vacancy lists in its own format. Not checked for any gmina. Load as CSV with `freePlaces` and `freePlacesAsOf`, `confidence` as the operator states it, and the list's URL as `sourceUrl`. |
| Gmina tuition tables (czesne) | OPEN | Published per gmina or per operator, in PDF or HTML. Not checked for any gmina. Load as CSV after a human has read the source. `method` is recorded as the loader's method, not as the source's. |
| Operator-submitted values | `unverified` | Values submitted by an operator or a parent are loaded only with `confidence = unverified`, and are shown as unverified. |
| Governing body (organ prowadzący) | UNVERIFIED | Only `GoverningBodyTypeCode` is stored. The `idTypOrgProw` dictionary is the same open item as §3.2. Private-individual codes 141, 146 and 150 yield the type only. The name is never read. |
| Licence of gmina files | OPEN | Check each gmina's terms before loading its files. Record the licence text with the URL and date. |

**Never used:** competitor sites (waszaedukacja.pl and similar), operator web pages fetched by the code, and any staff
or child personal data. A preschool value that cannot be traced to one of the sources above is not loaded.

**Verify before the first preschool load:** the SIO preschool headers (above), the licence of each gmina file, and
that each tuition value appears in its source with the same number. The check is a human read, not a model's output.

## 5. Phase B encodings: UNVERIFIED, and what the owner must check

Phase B encodes the following as **defaults in code**. They were written from the brief and from the Phase B
specification, in an environment that could not reach the publishers. Treat every item here as UNVERIFIED until
the owner checks it against the real publication.

### 5.1 SIO column map (`SioColumnMap`, UNVERIFIED default from the brief)

| Field | Default header |
|---|---|
| School id | `idPodmiot` |
| Entity type | `TypPodmiotu` |
| Name | `Nazwa` |
| Gmina TERYT | `TerytGmina` |
| Public or non-public | `Publiczność` |
| Pupil category | `KategoriaUczniow` |
| School specification | `SpecyfikaSzkoly` |
| Governing body type code | `idTypOrgProw` |
| Primary-school filter value for `TypPodmiotu` | `Szkoła podstawowa` |
| Class-level pupil and class columns | by pattern, from the map (ClassLevel to column) |

Behaviour that is fixed in code, whatever the real headers turn out to be:

- A loader **aborts the whole run** and writes nothing when a mapped required column is missing. The message lists
  the missing headers.
- The loader keeps only primary schools for children with pupils (the filter is part of the map).
- The loader never reads the organ-name column.

### 5.2 CKE column map (`CkeColumnMap`, UNVERIFIED)

- Join-key column default: `rspo`. **UNVERIFIED.** The CKE export may not contain a stable key at all.
- Matching: exact join only; unmatched rows are rejected and counted (ADR-0012). No fuzzy matching.
- Column names for the subject results and the sat counts: UNVERIFIED defaults until the export is read.

### 5.3 Other encoded assumptions

- `BaselineLoader` expects the csv columns `subject, nationalMeanPercent, pupilStdDevPoints` (the last optional
  in the file, but required for an interval). The column names are a Phase B format chosen by this project, not a
  publisher format.
- `GeoLoader` expects `idPodmiot, lat, lon, source, sourceUrl, asOf, method`. This is a project file format, so the
  coordinates come from a file the owner supplies, not from a geocoder (ADR-0010 stays Pending).
- `GoverningBodyDictionary` labels are null for the seeded codes, and `LabelVerified` is false for all of them.
  The natural-person flag is set for 141, 146 and 150 per the brief and is UNVERIFIED (§3.2).

### 5.4 Hosts unreachable from the build environment, 2026-10-11

The Phase B build environment could not reach these hosts on 2026-10-11, as reported by the Phase B build: the
dane.gov.pl dataset host, the CKE publication host and the geocoder providers. The RSPO API was probed directly
(§2). No other host was probed by the docs author. Nothing in Phase B was
fetched from them. Every value above was written from the brief, not from a live file.

## 6. What the owner must verify (in this order)

1. **SIO real headers.** Open the real `Szkoly_2025.xlsx` (or the year used), read the header row, and compare it with
   §5.1. Record each difference. Update the default map in code through a reviewed change. Confirm the
   primary-school filter value for `TypPodmiotu`.
2. **SIO source and licence.** Record the exact download URL, the retrieval date and the licence text (§1).
3. **idTypOrgProw dictionary.** Obtain the official code list (§3.2) before the natural-person flag is relied on.
4. **CKE export.** Locate the publication for the exam year. Record the format, the join key (exact column name and
   content), the sat counts (§3.6), the national means (§3.9), the licence, and the URL and date. Then run the
   verification in ADR-0012.
5. **Pupil standard deviation** (§3.8). Identify the source before any score is published.
6. **RSPO.** Re-probe from a reachable network (§2). Record the fields, the licence, and whether coordinates exist.
7. **Idempotency.** Run each import twice and record that row counts and values are identical.
