# Data sources research, October 2026

Access date for every item: 2026-10-14. Author: research agent, docs only.

## Method limits (read first)

**No primary publisher page could be read in this session.** The sandbox egress proxy rejected every Polish
government host and the map-vendor hosts (`CONNECT tunnel failed, response 403`; WebFetch returned
`getaddrinfo ENOTFOUND`). Hosts tried and failed: `api-rspo.mein.gov.pl`, `api.rspo.gov.pl`, `rspo.gov.pl`,
`dane.gov.pl`, `api.dane.gov.pl`, `cke.gov.pl`, `www.gov.pl`, `operations.osmfoundation.org`,
`mapsplatform.google.com`. Only WebSearch worked. It returns a model-written summary of search results, not
the page text.

Consequences:

- Nothing below is **VERIFIED** (read on the publisher's page this session). Confidence is **REPORTED**
  (search-result summary that cites the publisher or a secondary page) or **UNVERIFIED**.
- Quotes are short fragments as they appeared in search summaries. They are not confirmed against the page.
- No licence is stated as fact. Where a licence is unknown it says OPEN.
- To verify: re-run from a network that can reach the hosts (see `read_documentation` topic
  `environment.network`: the owner adds the hosts to Allowed domains), or open the URLs by hand.

## Summary table

| Source | What you get | Access | Licence | Freshness | Confidence | Recommendation |
|---|---|---|---|---|---|---|
| RSPO web search (`rspo.gov.pl`) | All schools and facilities, public and non-public, active and liquidated; map view | Public search; CSV or PDF export of results | OPEN | "updated daily" | REPORTED | Use later (manual check only) |
| RSPO API (`api.rspo.gov.pl`) | Register records (fields OPEN) | Formal application; credentials issued on approval; terms apply | OPEN (API terms) | Unknown | REPORTED | Use later, after the owner applies |
| SIO open datasets via dane.gov.pl (MEiN provider) | School-level statistics incl. the brief's "Szkoły" file | Download from dane.gov.pl | Brief says CC0; not seen | Yearly | UNVERIFIED | Use now (already the base), after the licence is read |
| SIO reports on request | Custom reports | Free for schools and some bodies, paid for others | Statute | On request | REPORTED | Avoid (paid, unnecessary) |
| CKE / OKE exam results | National results; OKE maps by voivodeship, powiat, gmina; school-level availability not established | CKE site, OKE sites | OPEN | Annual (July) | REPORTED (national), UNVERIFIED (school level) | Use later |
| EWD | Method documented for earlier cycles; no primary-school 8th-grade EWD found | n/a | OPEN | Old (2008 to 2016 material) | REPORTED | Avoid until a primary source is found |
| GUS BDL API | Education statistics at gmina level (SIO-derived) | REST API, free key | OPEN | Annual | REPORTED | Use later |
| TERYT (TERC, SIMC, ULIC) | Unit codes, localities, streets | eTeryt, full-file download | OPEN | Regular | REPORTED | Use now for codes (after licence read) |
| PRG (Geoportal) | Administrative units, address points, streets | Layer download, WFS, SHP/GML packages | Described as free and open; licence name not found | Updated; new address structure announced | REPORTED | Use now for boundaries and address points (after licence read) |
| Geoportal OpenLS geocoder (GUGiK) | Coordinates from Address Point object | OpenLS 1.2.0 profile endpoint | OPEN (Regulamin not read) | Follows PRG | REPORTED | Use later, after terms and quota check |
| Register of nurseries and clubs (Emp@tia, MRPiPS) | Per-facility address, fees, places, enrolled children, hours | Public map and register view; download not confirmed | OPEN | Operators update | REPORTED | Use later |
| Municipal nursery registers on `otwartedane.metropoliagzm.pl` | Per-gmina "Rejestr żłobków i klubów dziecięcych" (operator, NIP, REGON, address) | Open data portal, monthly update reported | OPEN per dataset | Monthly (reported) | REPORTED | Use later (GZM only) |
| Municipal open data (Warszawa, Kraków, Wrocław, Gdańsk) | Portals exist; relevant datasets not confirmed | Portals | OPEN | OPEN | UNVERIFIED | Use later, per city |
| Recruitment systems (Vulcan Nabór, Warszawa 19115 / WPR) | Parent-facing application and status systems | Login based | Vendor terms OPEN | Seasonal | UNVERIFIED | Avoid |
| dane.gov.pl API | Catalogue and resource access | `api.dane.gov.pl` | Per dataset | Per dataset | UNVERIFIED | Use now (for discovery) |
| Google Maps Platform | Geocoding, maps | Paid API with free caps | Service Terms | Live | REPORTED | Avoid for stored coordinates until terms are read |
| Nominatim public instance | Geocoding | Public API | ODbL data; usage policy | Live | REPORTED | Avoid for bulk; self-host or use files |
| Other geocoders (Mapbox, MapTiler, HERE, Photon, Pelias) | Geocoding | Various | Not researched | n/a | UNVERIFIED | OPEN |

## Per-source evidence

### 1. RSPO

- Official API page: `https://api.rspo.gov.pl/`. Search summary: the API "is for automating tasks", applications are
  handled within 14 days, approved applicants receive unique credentials, and the application states identity,
  contact person, intended use, estimated queries per day or month, data types, and acceptance of the API terms.
  Confidence REPORTED (search summary of the publisher's page, 2026-10-14). Not read.
- Public search: `https://rspo.gov.pl`. Summary: database updated daily, results shown on a map or saved as CSV or PDF.
  REPORTED.
- RSPO described as a module of SIO covering all schools and facilities, public and non-public, active and
  liquidated (ORE, CIE and KSDO pages appeared in results: `https://ore.edu.pl/2025/03/rejestr-szkol-i-placowek-oswiatowych-2/`,
  `https://cie.gov.pl/rspo/`, `https://ksdo.gov.pl/nasze-systemy/rejestr-szkol-i-placowek-oswiatowych/`). REPORTED.
- **Why the 2026-10-11 probe failed, two separate causes.** (a) The sandbox egress policy denies the host
  (`connect_rejected`). (b) The endpoint in `SOURCES.md` (`api-rspo.mein.gov.pl`) does not appear in any search
  result; the official page found is on `api.rspo.gov.pl`. The old host may be retired, and even a reachable host
  may need credentials. Older pages cite `rspo.men.gov.pl` as outdated. Neither cause is confirmed.
- Not found: response fields, coordinates, rate limits, licence text. All OPEN.
- Third party `adresy.app` advertises an RSPO-based API. Unofficial. Do not use.
- **Verify:** from an unrestricted network read `https://api.rspo.gov.pl/` fully, apply for access, then record the
  field list and whether lat/lon exist.

### 2. SIO and Ministry data at school level

- Statute: SIO law, consolidated text Dz. U. 2022 poz. 868 (`https://eli.gov.pl/eli/DU/2022/868/ogl/pol/pdf`).
  Summary quotes: "powszechny dostęp do informacji z zakresu oświaty". REPORTED.
- Ministry page `https://www.gov.pl/web/edukacja/system-informacji-oswiatowej-zasady-udostepniania-danych`: public
  statistical summaries are published by CIE, the Open Data Portal (provider filter MEiN) and GUS BDL. Individual
  reports are free for schools and certain bodies and paid for others. REPORTED.
- Not established: that the "Szkoły" file is dataset 288 under CC0 (the brief's claim), its URL, the header row,
  class sizes and staffing counts. Staffing is a risk area for personal data; see cautions. OPEN.
- **Verify:** open the dataset page on dane.gov.pl, record URL, licence field, format, file date, header row.

### 3. CKE / OKE exam results, EWD

- CKE has a page "Informacje o wynikach egzaminu ósmoklasisty" (`https://www.cke.gov.pl/wstepne-informacje-o-wynikach-egzaminu-osmoklasisty`)
  with a tab mentioning "wyniki według lokalizacji szkoły"; the snippet does not say what that contains. OPEN.
- Reported 2026 national means: about 65% Polish, 55% mathematics, 73% English, 393 338 pupils from 12 953 schools
  (press: `https://wydarzenia.interia.pl/egzamin-osmoklasisty/news-egzamin-osmoklasisty-2026-cke-publikuje-wyniki-i-dane,nId,23508858`).
  Secondary source only. Do not load as `NationalMeanPercent`; read CKE's own table (SOURCES 3.9).
- Search found OKE maps of means by voivodeship, powiat and gmina, not per school. Whether a per-school export with
  an RSPO key exists is **not established** (SOURCES 3.3, 3.6 stay OPEN).
- EWD: materials found are ORE and OKE documents from 2008 to 2016 on lower secondary and the sixth-grade test;
  a primary-school calculator based on the 2014 sixth-grade test is described. No source for 8th-grade EWD found.
  REPORTED, old. Do not plan on EWD for primary schools.
- Legal basis for publishing school-level results: **not found**. The search returned art. 9a of the education
  system act (exam information) but nothing on school-level publication. OPEN; do not state a legal basis.
- Pupil standard deviation (SOURCES 3.8): no publication found. OPEN.

### 4. GUS BDL, TERYT, PRG, Geoportal

- BDL API: documentation at `https://api.stat.gov.pl/Home/BdlApi?lang=en` and
  `https://api.stat.gov.pl/Content/files/bdl/Methods_Description_API_BDL_v1.pdf`. Summary: usable without
  registration at lower limits; free key sent by e-mail via header `X-ClientId`. Reported limits, anonymous /
  registered: 5 / 10 per second, 100 / 500 per 15 min, 1 000 / 5 000 per 12 h, 10 000 / 50 000 per 7 days.
  REPORTED; confirm in the PDF. Licence OPEN.
- TERYT: components TERC, SIMC, ULIC (streets), NOBC (addresses), BREC (statistical regions). A "full files"
  download on stat.gov.pl is suggested by a secondary source. Licence OPEN. REPORTED.
- PRG: `https://www.geoportal.gov.pl/pl/dane/panstwowy-rejestr-granic-prg/`. Summary: free and available for any
  use (wording as summarised, licence name not found); downloads by layer tree, WFS "Państwowy Rejestr Granic –
  Jednostki Terytorialne", and packages "PRG adresy" and "PRG jednostki administracyjne" in SHP and GML. Address
  data moved to the structure under the 21 July 2021 regulation on the register of localities, streets and
  addresses (`https://www.geoportal.gov.pl/aktualnosci/dane-adresowe-dostepne-do-pobrania-w-nowej-strukturze/`).
  A secondary page dates the change 2026-09-10. REPORTED. The ADR-0012 style join and the importer must handle the
  structure change.
- Coordinates therefore have a candidate source without any commercial geocoder: PRG address points matched to
  an SIO address, with a documented match rule. Not tested.
- BDOT10k: not researched. OPEN.
- Street-list catchments (obwody) are per gmina and outside these registers (SOURCES 3.7).

### 5. Nurseries and clubs

- Register run by the wójt, burmistrz or prezydent per place of operation; filings go through the MRPiPS portal
  Emp@tia (`wnioski.mpips.gov.pl`). A public map at `empatia.mpips.gov.pl/web/piu/mapa-zlobkow-i-klubow-dzieciecych`
  is reported. A government document says the register shows address, fees, places, enrolled children and
  hours. REPORTED. Download or API availability, licence and legal basis: OPEN (SOURCES 4B.1.1, 4B.1.2).
- Municipal copies exist: `https://otwartedane.metropoliagzm.pl` hosts per-gmina datasets (Katowice, Tychy,
  Mikołów, Sośnicowice) with operator name, NIP, REGON, address; update reported as monthly. Licence not read.
  Operators who are natural persons appear with NIP: see cautions. REPORTED.
- Warsaw: kindergarten map `https://mapa.um.warszawa.pl/przedszkola/` and 19115 pages exist; an open dataset and
  licence were **not found**. Kraków, Wrocław, Gdańsk: portals exist (Wrocław open data service, Gdańsk CKAN);
  relevant datasets not found. Poznań: nothing found. UNVERIFIED.

### 6. Recruitment data

- Vulcan "Nabór" is software for nurseries, preschools and schools (Regulamin usług powiązanych NABOR 2018:
  `https://www.vulcan.edu.pl/storage/file/core_files/2022/6/20/c24fdb6901ab8a016c29116f7897052c/Regulamin-uslug-powiazanych-w-ramach-uslugi-NABOR-2018.pdf`).
  Search found no scraping clause. That is absence in a summary, not proof. Applications contain child and parent
  data. Treat all candidate-level data as off limits; use only published aggregate lists with a stated licence.

### 7. dane.gov.pl and the open-data law

- Ministry sources say MEiN statistics are on the open data portal (`https://dane.gov.pl/dataset`). API
  documentation page not read. Per-dataset licence. OPEN.
- Law: act of 11 August 2021 on open data and re-use of public sector information, consolidated text
  Dz. U. 2023 poz. 1524 (`https://lexlege.pl/ponowne-wykorz-inf-sekt-publ/rozdzial-1-przepisy-ogolne/14930/`,
  secondary). Reported: it implements the EU directive, sets a regime for "high-value data" with API access for
  datasets on the EU list, and Implementing Regulation 2023/138 applies since 2024-06-09. Which terms of re-use
  (licence, fees, attribution) apply to a given dataset is set per dataset; the paragraph on what "reuse"
  allows cannot be written from verified text. OPEN: read arts. 2 to 14 of the consolidated text on ISAP.

### 8. Geocoding and maps

- Google Maps Service Terms (`https://cloud.google.com/maps-platform/terms/maps-service-terms`): search
  fragments show a 30-day limit on caching listed address and latitude/longitude content, with no exception
  seen. Place ID rule not seen. REPORTED. Pricing: secondary sources say Geocoding has 10 000 free calls per
  month per SKU since March 2025; the official pricing page was **not** read. Free-tier facts: OPEN.
  Design consequence: do not store Google-derived coordinates in the database.
- Nominatim: official policy `https://operations.osmfoundation.org/policies/nominatim/` not read. Secondary
  sources: about 1 request per second, bulk geocoding not allowed, identifying User-Agent, caching expected,
  attribution required. REPORTED.
- Geoportal OpenLS (`http://mapy.geoportal.gov.pl/openLSgp/geocode`, doc page
  `https://www.geoportal.gov.pl/pl/usluga/usluga-openls/`): geocodes against PRG Address Points. Regulamin,
  quotas and storage rules not read. A forum post (not official) mentions limits on commercial re-use. ULDK is
  for cadastral parcels, not addresses. REPORTED.
- Mapbox, MapTiler, HERE, Photon, Pelias: not researched. OPEN.
- ADR-0010 stays Pending. A defensible path to check: PRG address points as a file, loaded through `GeoLoader`.

### 9. Public transport and walking time

Skipped; no evidence it is needed for the first edition.

## What we still cannot verify

1. Any licence text, for every source above (no page was read).
2. RSPO API field list, coordinates, rate limits, the current host and the approval terms.
3. That the SIO "Szkoły" file is CC0, its URL and its headers; class and staffing fields.
4. Whether CKE/OKE publishes per-school exam results, sat counts, national means and pupil standard deviation.
5. Legal basis for publishing school-level results.
6. Primary-school EWD availability.
7. Nursery register: download, API, fields, licence, legal basis.
8. Municipal open datasets for the large cities, and catchment lists.
9. Google official pricing, Nominatim official policy, OpenLS terms, other geocoders.
10. dane.gov.pl API terms and the exact wording of the reuse provisions.
11. BDOT10k, BDL licence.

## Legal and ethical cautions

- **Small cohorts:** school-level results for few pupils can re-identify children. The project threshold (ADR-0009)
  applies to every new source, including nursery and recruitment figures.
- **Pupil-level and personal data:** recruitment systems hold child and parent data. Never ingest it. Nursery
  registers may name sole-trader operators with NIP or REGON; treat these as personal data of a natural person and
  do not publish them without a decision (same logic as codes 141, 146, 150).
- **Staffing counts:** use aggregates only; no teacher entity (AGENTS.md).
- **Scraping:** do not scrape recruitment portals, competitors or operator pages. Use files and registers with a
  stated licence; apply for the RSPO API instead of crawling the search page.
- **Attribution:** each value keeps `source`, `sourceUrl`, `asOf`, `method`. OSM data needs attribution if used.
- **Caching:** do not persist Google-returned coordinates beyond the terms; prefer PRG or file-based coordinates.
- **Currency:** an old vacancy or fee list is labelled old, never current (SOURCES 4B.1.4).
- **No invented legal basis:** the product copy must not cite a statute for school-level publication until it is read.
