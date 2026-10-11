# UI and UX

Screens and flows as scaffolded in Phase A, then the ranked backlog. Product UI copy is Polish. This document
is in English. Labels below are the intended Polish labels where known, and are marked "(PL, to confirm)" otherwise.

## Screens scaffolded in Phase A

| Route (proposed; `web/app` is authoritative once written) | Purpose | Phase A state |
|---|---|---|
| `/` | Entry: address or school search box | Polish stub, no address search (ADR-0010 pending) |
| `/szkola/[rspo]` | School profile: identity, type, location, the data this portal holds | Stub, reads the catalog API |
| `/metodologia` | Methodology page, versioned, with the reading guide for score and confidence | Content in place; marked draft |
| `/dla-szkol` | For schools: what is shown, how to request a correction | Stub |
| `/sitemap.xml`, `/robots.txt` | Search engine surface | Generated |

Other routes from the brief (catchment result, comparison, ranking) are Phase B and C. They are not scaffolded
as working screens.

## Phase C status

Phase C (public search surface) is in progress. Written in Phase C, not yet compiled or run in CI at the time of
writing: the API read endpoints, the catchment and correction endpoints, the ranking and compare contract, the
rate limits, and the web pages that consume them. The journey is covered by a Playwright suite
(`tests/e2e/tests/journey.spec.ts`) that runs against a synthetic demo gmina, `Przykładowo` (TERYT 9999999), seeded
when `SCHOOLSEARCH_DEMO_SEED=true` and never in Production (`src/SchoolSearch.Api/Demo/`). Every demo row is
labelled `DEMO SEED (synthetic)`. The demo data is fictional and must never be read as a result.

| Area | Phase C state | Proof |
|---|---|---|
| Profile: value, source, as-of, method on every row | Contract in place; web page in progress | e2e: provenance table names the demo seed |
| Score with interval, cohort size and methodology version | Contract in place | e2e: home catchment result |
| `insufficient_cohort` (8 pupils) | Explicit message, no number | e2e: "zbyt mała grupa", no `±` on the page |
| Catchment by street and number (`street_list`) | Gmina-level street list only; no polygon, no distance guess | e2e: match and the `no data` state |
| Gmina with no catchment data | Explicit "brak danych", no guessed school | e2e: `Przykładowo-Wschód` |
| Ranking (country) | Descending score only; no sort control | e2e: non-increasing scores, no sort control |
| Compare (2 to 4 schools) | Same attribute rows for each school | e2e: three columns |
| Correction request | Token returned on submit | e2e: token in the status line |
| Methodology | States that it is not an official ranking | e2e |
| Phone-width layout, accessibility scan | Not yet covered by a test | Backlog ranks 7 and 8 |

Not yet tested in CI: the trend chart is checked only as a visible `svg`; the axe scan and the phone-width viewport
test are not written yet.

## Flows

1. **Find a school by name or place (Phase A/C):** search box, results list, school profile.
2. **Read a profile (Phase A/C):** identity, then the data block. Every value shows its source, as-of date and
   method. A score, when present, is shown together with its cohort size and confidence in the same block.
3. **Read a limited-confidence profile (Phase B/C, ADR-0009):** shows descriptive data, the sample size and an
   explicit line that no score or rank is published for this cohort. It never shows a rank.
4. **Read insufficient cohort (Phase B/C):** shows the cohort size and the reason. It shows no value and no zero.
5. **Address to catchment school (Phase C, gated by ADR-0010 and catchment availability):** not scaffolded as working.
6. **Correction request (Phase C):** a school or parent submits a correction, which an operator reviews through an
   admin endpoint (ADR-0008).
7. **Compare two schools (Phase C):** side by side, same cohort, same methodology version.

## Ranked backlog

Ranked by user harm if wrong first, then by how much the next phase depends on it. Each item states the gate it
must pass.

Re-ranked at Phase C. Ranked by user harm if wrong first, then by how much the next step depends on it. The
Status column shows what Phase C delivered; open items are the ones still gated.

| Rank | Item | Why this rank | Gate | Status (Phase C) |
|---|---|---|---|---|
| 1 | `insufficient_cohort` shown as an explicit message, never 0 or blank (8 pupils) | A zero reads as a bad school | e2e 8-sat profile | Done in the e2e suite; needs its first CI run |
| 2 | Score never shown without cohort size, interval and methodology version in the same block | Prevents comparing noise (METRIC-ETHICS §3) | e2e catchment result; API contract tests | Done in the e2e suite; needs its first CI run |
| 3 | Every value shows source, as-of date and method | A value without provenance is the most damaging failure | e2e profile provenance table | Done in the e2e suite; needs its first CI run |
| 4 | Gmina with no catchment data shows "brak danych", never a guessed school | A guessed school is a false statement about a child's school | e2e no-data gmina | Done in the e2e suite; needs its first CI run |
| 5 | Methodology page: "nie jest oficjalnym rankingiem", reading guide, version, draft status | Users need to know what a score means before reading one | e2e methodology text | Text checked; reading guide still in review |
| 6 | Correction path for schools (`/dla-szkol`) | Required for a public data portal | e2e token on submit; admin flow after ADR-0008 | Token path checked; operator review is manual |
| 7 | Limited-confidence profile band (ADR-0009: cohort 5 to 8) | Keeps about 1 550 schools informative without a rank | Boundary tests at 4, 5, 8, 9 | Boundary data seeded (8 and 9 pupils); band UI not yet built |
| 8 | Phone-width layout with no horizontal scroll | Most searches happen on phones | Playwright viewport test | Not started |
| 9 | Accessibility pass: keyboard, contrast, screen-reader labels on all states | Required for a public service | axe check in Playwright | Not started |
| 10 | Catchment by street and number (`street_list` only; no polygon, no distance guess) | The wedge; the gmina decides the boundary | Gmina resolution linked; ADR-0010 | Working for the demo gmina; real gminas wait on data |
| 11 | Compare two to four schools | Useful after profiles are trusted | e2e three-column compare | Working; needs its first CI run |
| 12 | Ranking list (country and gmina) | Only after cohort rules and methodology are final | e2e descending scores and no sort control | Working; needs Phase B methodology sign-off |
| 13 | Preschool cost and availability (no quality score) | Phase D; a different data shape | Phase D scope | Not started |
| 14 | Parent reviews and accounts | Phase E; DSA machinery required first | authservice wired; DSA terms | Not started |

## Rules for every screen

- Anti-goal in the UI: no per-person view and no sort that orders schools by an individual's data.
- No ascending sort on a composite score.
- Confidence and cohort size appear in the same component as the score.
- The product never says "oficjalny ranking".
