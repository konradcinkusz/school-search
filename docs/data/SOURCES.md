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

## 4B. Other levels: nurseries and the later edition (ADR-0021, Proposed)

Nothing in this section is verified. Each item is OPEN, with how to verify it. No coverage count, licence term, register
format or legal basis is stated here, because none has been checked. Nursery items (4B.1) are needed before nurseries
are loaded. Items 4B.2 onward are needed only if the owner starts the second edition (ADR-0021, trigger).

### 4B.1 Nurseries (żłobki, children aged 0 to 3) and children's clubs (kluby dziecięce)

| # | Item | Status | Why it matters | How to verify |
|---|---|---|---|---|
| 4B.1.1 | National register of nurseries and children's clubs | OPEN, UNVERIFIED that such a register exists in the form assumed here. Believed to be kept by the ministry responsible for family and labour policy and by the voivodes; this is not checked. | The only candidate for a national list of nursery locations and operators. Without it, nurseries are known only gmina by gmina. | Identify the register's publisher from the publisher's own pages, not from this file. Record the exact URL, whether it is downloadable or only browsable, the export format (CSV, XLSX, API, PDF), the licence text, the retrieval date, and the header row or field list. Confirm whether it carries coordinates, opening hours and the operator's type. Confirm whether it lists clubs as well as nurseries, and whether they can be told apart. |
| 4B.1.2 | Legal basis of the register and of its publication | OPEN | Needed before the product states what the register is or who keeps it. The product must not state a legal basis from memory. | Read the statute that establishes the register on the publisher's page, and record its title, date and article from the source, with the URL. |
| 4B.1.3 | Municipal fee lists for nurseries (opłaty za żłobek, opłata za wyżywienie) | OPEN | Fees are a practical fact parents need. They are set per gmina or per operator and may be published only as PDF or HTML. | Check each gmina's published resolution or fee table for format, licence and date. For each value, record `value`, `currency`, `sourceUrl`, `asOf` and `method` (the loader's method, not the source's). Load as CSV only after a person has read the source. Do not assume a fee is the same across gminas. |
| 4B.1.4 | Municipal vacancy lists for nurseries (wolne miejsca) | OPEN | Free places change often and are published in different formats and at different times, if at all. | Check each gmina's published lists: format, update frequency, whether counts are per facility, and the licence. Record the as-of date of each list. An old list is shown as old, never as current. |
| 4B.1.5 | RSPO coverage of nursery-age facilities | OPEN. Expected to be absent, but **not verified**. | If RSPO holds nurseries, it would be a second register with a stable key. If it does not, the national list in 4B.1.1 is the only candidate. | After the RSPO probe in §2 succeeds, list the facility types the API returns. Record whether any type code or name refers to a nursery, a children's club or a nursery-age unit. Record the absence as a finding only when the full list is read, not from a sample page. |
| 4B.1.6 | Licence of municipal files (fees, vacancies) | OPEN | Each gmina may publish under a different licence or none. | Read each file's licence or terms on the page where it is published. Record the text, the URL and the date. A file without a stated licence is not loaded without the owner's decision. |
| 4B.1.7 | Opening hours and location of nurseries | OPEN | Hours and address are the first facts a parent needs. Their source is not known. | Find them in the register (4B.1.1) or in the operator's own published information. If an operator page is the only source, record it as `unverified` (ADR-0014). Never scrape it. |

### 4B.2 Secondary schools (liceum, technikum, szkoła branżowa), for the later edition only

| # | Item | Status | Why it matters | How to verify |
|---|---|---|---|---|
| 4B.2.1 | RSPO records for secondary schools | OPEN. The RSPO API is unreachable from the build environment (§2). | Secondary types must be known before any secondary profile exists. | After the §2 probe succeeds, record the school types returned, their type codes and names, and whether secondary types are present. Do not infer type codes from primary ones (§3.2). |
| 4B.2.2 | CKE matura results at school level | OPEN. Whether CKE publishes school-level matura results at all is not established. | Matura is the main public outcome figure for secondary schools, and the most gamed. Without school-level data there is no basis for any profile. | Find the CKE publication for the matura year. Record whether results are published per school, the format, the join key and its content, the suppression rule for small groups, the licence text, and the URL and date. Then run the ADR-0012 verification again, for matura, as a separate join. |
| 4B.2.3 | Sat counts for matura, per school | OPEN | The cohort size and the coverage counter-metric (METRIC-ETHICS, MASTER-PROMPT) depend on it. A result without its cohort is not usable. | Find whether CKE publishes the number of candidates per school and per subject, and under what licence. Record it with the URL and date. |
| 4B.2.4 | Licence and terms of the CKE and OKE publications | OPEN | The reuse of any figure depends on its licence. | Read the publisher's terms on each publication page. Record the text, the URL and the date. |

### 4B.3 Universities (graduate outcomes), for the later edition only

| # | Item | Status | Why it matters | How to verify |
|---|---|---|---|---|
| 4B.3.1 | ELA graduate-outcome data (the graduate-outcomes survey named ELA in the owner's notes) | OPEN, UNVERIFIED as a usable public source. Its format, licence, granularity and suppression rule are not known. | Candidate source for employment and earnings of graduates by university and field. | Identify the publisher's page for the survey. Record whether data is published per university and per field, the file format, the licence text, the suppression rule for small groups, the URL and the date. Confirm that no individual graduate is identifiable. |
| 4B.3.2 | POL-on data for universities | OPEN, UNVERIFIED as a usable public source. Its public output and licence are not known. | Candidate for basic facts about universities and programmes (existence, field, form of study). Not an outcome measure by itself. | Identify which POL-on figures are published openly, and where. Record the format, licence, the URL and the date. Confirm whether programme-level data is available without a login. |
| 4B.3.3 | Outcome measures for universities | OPEN | Any university-level figure needs a methodology, a cohort size and an interval, as for schools. None is defined. | Defined only if 4B.3.1 or 4B.3.2 is verified. Then a methodology is written per level, and it passes the METRIC-ETHICS review before any value is published (ADR-0021 trigger). |

### 4B.4 Rules that apply to these items

- Do not treat a candidate in 4B.3 as a source until its licence and format are recorded here with a URL and a date.
- A figure from any item above, once loaded, carries `source`, `sourceUrl`, `asOf`, `method` and, for an operator
  value, `confidence` (ADR-0014).
- Nothing is fetched or scraped from a competitor. The items above name publishers, not competitor sites.

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
