# Import runbook

How the catalog is loaded. The importer is a one-shot console app with its own image. It is not a second
Api process and it never serves traffic.

Status: **not run against real data.** The public sources are not reachable from the build environment,
so every column name in the column maps is UNVERIFIED (see docs/data/SOURCES.md). Nothing in this runbook
is a verified fact about a file until someone has run the importer on the real file and recorded the result.

## 1. What runs where

- **Image:** `ghcr.io/konradcinkusz/school-search-import`, built from `src/SchoolSearch.Import/Dockerfile`
  (context: repository root). Entrypoint `dotnet SchoolSearch.Import.dll`; the arguments select a command.
- **Not used:** running the import as a one-off command on the Api image. The Api image is for serving.
- **Machine:** a one-shot machine (restart policy `no`) started on demand, with the import image, the
  database connection and the input file made available to it. How the file reaches the machine is an
  open ops decision; no path or file is baked into the image. Creating the machine is not part of the code
  in this repository and was not done by any agent.
- **Database:** `catalogdb` on `school-search-postgres`. The Api owns the schema and applies migrations at its
  startup. The importer never migrates; it refuses to write if the schema is not the one it was built for.

## 2. Configuration

| Variable | Value |
|---|---|
| `DATABASE_PROVIDER` | `postgres` for a run. `inmemory` is a dry run that starts empty and writes nothing persistent. Unset or any other value is an error. |
| `ConnectionStrings__catalogdb` | Required with `postgres`. Secret: supplied by the platform, never in the repository. |

## 3. Commands

| Command | Input | Writes |
|---|---|---|
| `check-schema` | none | nothing; prints whether the schema is compatible |
| `sio --file <xlsx> --year <yyyy> [--as-of yyyy-MM-dd] [--map <json>]` | SIO school register, xlsx | Schools, ClassCohorts, GoverningBodyTypes (seed) |
| `cke --file <csv> --year <yyyy> [--map <json>]` | CKE per-school results, csv | ExamResults |
| `baseline --file <csv> --year <yyyy>` | national baseline csv (`subject,nationalMeanPercent,pupilStdDevPoints`) | NationalBaselines |
| `geo --file <csv>` | coordinates csv (`idPodmiot,lat,lon,source,sourceUrl,asOf,method`) | Schools coordinates and their provenance |
| `catchment --file <csv>` | catchment street-list csv (section 8) | CatchmentStreets, CatchmentCoverages |

`--as-of` pins the date the SIO file describes. Without it the run date is used, which is usually wrong
for a file exported earlier. Record the real export date.

`--map` loads a JSON override for the column map. Unknown JSON properties are an error, so a misspelt
key cannot silently fall back to a default. The built-in defaults are UNVERIFIED.

Every run also writes one `ImportRun` row (kind, start time, finish time, counts, schema version).
A row with an empty `FinishedAt` is a run that did not complete: the loader threw, or the process was stopped.
A run that failed midway may have written part of its rows, so re-run the same command; the import is idempotent.

## 4. Calendar

Per the product brief (not re-verified against the publishers):

- **SIO school register:** imported in **February**, when the register for the school year is published.
- **CKE results:** imported in **July**, after the exam results are published. The baseline file for the same
  year is loaded with it.
- **Geo coordinates:** loaded when a file is available. No geocoder is invoked by the importer; the geocoding
  source is ADR-0010 and is still pending.

## 5. Procedure

1. Record the source URL and the retrieval date of the file. Check its licence on the publisher's page.
2. `check-schema`. Exit 2 means the Api has not migrated the database yet: stop and report it.
3. Run the file's command. Read the counts: `read`, `written`, `rejected`.
4. Run the **same command a second time**. The row counts must be identical and nothing new may be written.
5. Check the SIO class VIII figures against docs/data/SOURCES.md §1 (per brief: at least 5 pupils 12 564 schools;
   at least 9 pupils 11 014 schools). A mismatch is a finding to record, not a number to force.
6. Record the run ids and the counts in the change or the data log.

## 6. Exit codes

| Code | Meaning | Action |
|---|---|---|
| 0 | Run finished. Rejected rows are counted and listed, and do not change the code. | Read the rejection reasons. |
| 1 | Error: bad arguments, configuration, a missing mapped column, a personal-data column, or an I/O or database failure. No catalog rows are written; the ImportRun row stays unfinished (empty FinishedAt). | Fix the input or the setting and re-run. |
| 2 | Schema mismatch: `__EFMigrationsHistory` lacks the expected migration. Nothing is written. | Apply the Api migration first. |

## 7. Rules the importer enforces

- **No names.** The SIO organ-name column is never read. Governing body is stored as its type code only.
  Codes 141, 146 and 150 mean a private individual per the brief (unverified against the official dictionary).
  A file with a personal-data column (for example `imie`, `nazwisko`, `pesel`) is rejected whole.
- **Faith flag** is derived from governing body type 140 only, never from a school name or patron.
- **Join keys are exact.** A CKE row whose key does not match a school exactly is rejected and counted, never
  matched fuzzily. The CKE join key is UNRESOLVED (ADR-0012 pending).
- **Missing mapped column:** the run aborts with the list of missing headers and writes no catalog rows (the ImportRun row stays unfinished).
- **Provenance:** every written row carries source, source URL, as-of date and method.

## 8. Catchment (obwody) CSV

Command: `catchment --file <csv>`. The file is a street list taken from a gmina's resolution (uchwała) that
assigns streets, or number ranges on streets, to a primary school's catchment zone. The importer copies what the
file says. It never derives a boundary from distance or coordinates, and it never guesses a street.

Status: **no catchment file has been loaded.** Whether a given gmina publishes a street list at all is an open
item (MASTER-PROMPT section "Unknowns": catchment availability per gmina). Record each gmina's coverage in the
file, including `none`, so the public page can say "no data" truthfully.

### 8.1 Format

Header row required. Delimiter `,` or `;` (detected from the header). UTF-8, with or without BOM. Column names
are matched case-insensitively, and every column below must be present (a missing one aborts the run with nothing
written). Extra columns are ignored, except that a pupil or personal-data column aborts the whole file.

| Column | Required | Meaning |
|---|---|---|
| `gminaTeryt` | yes | 7-digit TERYT code of the gmina. |
| `schoolIdPodmiot` | for `street_list` | SIO idPodmiot of the school (exact integer, no fuzzy matching). Must match an existing School. Empty for `none`. |
| `street` | for `street_list` | Street name as the resolution writes it, for example `ul. Przykładowa`. Normalised by the importer (section 8.3). Empty for `none`. |
| `numberFrom` | no | First house number of the rule (whole number, 1 to 999999). Empty = from the start of the street. |
| `numberTo` | no | Last house number of the rule (whole number, 1 to 999999, not below numberFrom). Empty = to the end of the street. |
| `parity` | no | `even`, `odd`, or empty for both sides. |
| `state` | yes | `street_list` or `none`. `polygon` is not loaded by this command. |
| `resolutionUrl` | for `street_list` | Absolute http(s) link to the resolution. The public page shows it. Optional for `none`. |
| `resolutionDate` | no | Date of the resolution, `yyyy-MM-dd`. |
| `source` | yes | Name of the publisher or document (at most 200 characters). |
| `sourceUrl` | yes | Absolute http(s) URL where the file was taken from (at most 500 characters). |
| `asOf` | yes | Date the file describes, `yyyy-MM-dd`. Must not be later than the run date. |
| `method` | yes | How the rows were transcribed, for example `manual transcription from the resolution annex` (at most 500 characters). |

Example (fictional; gmina code and names are placeholders, not a real gmina):

```
gminaTeryt,schoolIdPodmiot,street,numberFrom,numberTo,parity,state,resolutionUrl,resolutionDate,source,sourceUrl,asOf,method
9900001,1001,ul. Przykładowa,,,,street_list,https://example.invalid/uchwala-1,2026-01-15,Przykladowa uchwala,https://example.invalid/uchwala-1,2026-09-01,manual transcription
9900001,1002,ul. Testowa,1,20,odd,street_list,https://example.invalid/uchwala-1,2026-01-15,Przykladowa uchwala,https://example.invalid/uchwala-1,2026-09-01,manual transcription
9900003,,,,,,none,,,Przykladowy urzad,https://example.invalid/none,2026-09-01,manual transcription
```

### 8.2 Rules the importer enforces

- **Coverage fields are per gmina.** Every row of one gmina must carry the same `state`, `resolutionUrl`,
  `resolutionDate`, `source`, `sourceUrl`, `asOf` and `method`. Coverage is stored once per gmina.
- **Atomic per gmina.** If any row of a gmina is rejected (unknown school, bad number, bad URL, mismatched
  coverage fields and so on), nothing is written for that gmina, and its previously loaded streets and coverage
  stay in place. Rejected rows are listed with their line number. Fix the file and run it again.
  A half-written street list would make a missing street look like "no school", which is why the whole gmina waits.
- **Other gminas are replaced in full.** For each gmina with no rejected rows, its existing streets are deleted
  and the file's streets are inserted, in one save. Re-running the same file gives the same rows and counts.
  A gmina absent from the file is not touched.
- **Exact duplicate rows** (same gmina, state, school, street, numbers and parity) are ignored with a reason.
- **Unknown school ids are rejected**, never matched by name or nearby id.
- **Personal data:** a header containing a name, PESEL or citizenship column aborts the whole file.
- The importer writes one `ImportRun` row (kind `catchment`) with the counts, as for every command.

### 8.3 Street normalisation

The importer and the Api use the same rule, so a street typed by a parent matches the stored name:

1. Lower-case (invariant culture).
2. Fold Polish diacritics, including `ł` to `l`, and common Latin accents, through a fixed table.
3. Replace every character that is not a letter or digit by a space, and collapse runs of spaces.
4. Remove one leading street-type word: `ul`, `ulica`, `al`, `aleja`, `pl`, `plac`.

Examples: `ul. Przykładowa` gives `przykladowa`; `Al.  Łąkowa` gives `lakowa`; `Jana Pawła II` gives `jana pawla ii`.
The golden list is in `tests/SchoolSearch.Import.Tests/CatchmentLoaderTests.cs`. The copy in
`src/SchoolSearch.Api/Catchment/StreetNormalizer.cs` must stay identical.

### 8.4 What the public API says

`GET /api/v1/catchment?gmina=&street=&number=`. A gmina with no `street_list` coverage (no row, or `none`) gets
`match: "no_data"`, with an empty school list. The response never implies a boundary the file does not contain.
The house number is the leading digits of `number` (`12A` is 12). A street with a number range or parity needs
`number` to match. The disclaimer and the resolution link are shown with every answer.
