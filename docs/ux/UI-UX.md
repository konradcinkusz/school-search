# UI and UX

Screens and flows as scaffolded in Phase A, then the ranked backlog. Product UI copy is Polish. This document
is in English. Labels below are the intended Polish labels where known, and are marked "(PL, to confirm)" otherwise.

## Screens scaffolded in Phase A

| Route (proposed; `web/app` is authoritative once written) | Purpose | Phase A state |
|---|---|---|
| `/` | Entry: address form (gmina, street, number) with the catchment panel, and a school name search below it | Address form and catchment panel in place (`web/app/src/app/page.tsx`); name search reads the catalog API |
| `/szkoly/[slug]` | School profile: identity, type, location, the data this portal holds (was `/szkola/[rspo]`) | Reads the catalog API |
| `/metodologia` | Methodology page, versioned, with the reading guide for score and confidence | Content in place; marked draft |
| `/dla-szkol` | For schools: what is shown, how to request a correction | Stub |
| `/robots.txt`, `/sitemap/<id>.xml` | Search engine surface. There is no `/sitemap.xml`; `robots.ts` lists ids 0 (static pages), 1 to 5 (school profiles) and 6 (preschool profiles) | Generated (`web/app/src/app/robots.ts`, `sitemap.ts`, `lib/sitemap-config.ts`) |

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
| `limited` (5 to 8 pupils who sat) | Profile with cohort size and subject averages; explicit line; no score, interval or rank | e2e: 8-sat school shows the limited profile (`journey.spec.ts`); `limited-band.spec.ts`; not yet run in CI |
| `insufficient_cohort` (below 5 pupils) | Explicit message, no number | e2e: "zbyt mała grupa", no `±` on the page (no spec for this case found in `tests/e2e/tests`) |
| Catchment by street and number (`street_list`) | Gmina-level street list only; no polygon, no distance guess | e2e: match and the `no data` state |
| Gmina with no catchment data | Explicit "brak danych", no guessed school | e2e: `Przykładowo-Wschód` |
| Ranking (country) | Descending score only; no sort control | e2e: non-increasing scores, no sort control |
| Compare (2 to 4 schools) | Same attribute rows for each school | e2e: three columns |
| Correction request | Token returned on submit | e2e: token in the status line |
| Methodology | States that it is not an official ranking | e2e |
| Phone-width layout, accessibility scan | Not yet covered by a test | Backlog ranks 7 and 8 |

Not yet tested in CI: the trend chart is checked only as a visible `svg`; the axe scan and the phone-width viewport
test are not written yet.

## Phase I status

Phase I redesigned the UI. The visual system is in `docs/ux/DESIGN-SYSTEM.md`: plain CSS in `web/app/src/app/globals.css`,
phone first at 360 px, with the score, its interval and the caveats kept in one block. A visual harness in
`tests/e2e/visual` visits every public route at 360, 768 and 1366 px and writes screenshots and findings. It runs
locally against a mock catalog, not in CI, and it asserts only that pages load. CI on `main` was green through Phase I
(PR #10, 13 checks). Still open: a CI gate for phone-width overflow (row 8), an accessibility pass with an axe check in
CI (row 9), the limited-confidence band (row 7, built; its tests have not run in CI yet), and live runs of the AI features and of the authservice login (rows 14
and 15).

## Flows

1. **Find a school by name or place (Phase A/C):** search box, results list, school profile.
2. **Read a profile (Phase A/C):** identity, then the data block. Every value shows its source, as-of date and
   method. A score, when present, is shown together with its cohort size and confidence in the same block.
3. **Read a limited-confidence profile (5 to 8 pupils who sat, ADR-0009):** shows the cohort size, the subject averages
   with their pupil counts, and the explicit line "Dla tej grupy nie publikujemy wyniku ani miejsca w zestawieniu."
   It shows no score, no interval and no rank. It never shows a rank.
   *Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*
4. **Read insufficient cohort (fewer than 5 pupils who sat, ADR-0009):** shows the cohort size and the reason. It shows
   no value and no zero.
5. **Address to catchment school (Phase C, gated by ADR-0010 and catchment availability):** the home page has the address form and the catchment panel. A street and number in a gmina with street data shows the catchment school; a gmina without catchment data shows a note that the gmina sets the boundary and the product does not estimate it by distance.
6. **Correction request (Phase C):** a school or parent submits a correction, which an operator reviews through an
   admin endpoint (ADR-0008).
7. **Compare two schools (Phase C):** side by side, same cohort, same methodology version.

## Levels

The product is organised in levels of schooling, shown to the parent as a choice of stage, not as separate sites.
Level 1 covers childcare: preschools now, nurseries (żłobki, ages 0 to 3) if the owner accepts ADR-0021. Level 1 shows
practical facts only, each with its source and as-of date, and never a quality score, rank or "best" label. Level 2 covers
primary schools and shows scores with their interval, cohort size and methodology version. A second edition for secondary
students and university applicants is deferred and has no screens here. Its trigger and the open decisions are in
[`docs/adr/0021-product-scope-levels-and-nurseries.md`](../adr/0021-product-scope-levels-and-nurseries.md) (status
Proposed). Any new level is a new `kind` or `educationLevel` value in the catalog, not a new product.

Research on demand, the required content per level, legal checks and funding options is in [`docs/research/2026-10-portal-requirements.md`](../research/2026-10-portal-requirements.md) (2026-10-14; figures there are REPORTED or UNVERIFIED, not read at primary sources). Its gap table ranks recruitment calendars, fees and vacancies as the next content to add.

## Ranked backlog

Ranked by user harm if wrong first, then by how much the next phase depends on it. Each item states the gate it
must pass.

Re-ranked at Phase C. Ranked by user harm if wrong first, then by how much the next step depends on it. The
Status column shows what Phase C delivered; open items are the ones still gated.

| Rank | Item | Why this rank | Gate | Status (Phase C) |
|---|---|---|---|---|
| 1 | `insufficient_cohort` shown as an explicit message, never 0 or blank (fewer than 5 pupils) | A zero reads as a bad school | e2e below-5 school (no spec yet; `journey.spec.ts` covers the 8-sat `limited` case) | Message in the UI; e2e job green on PR #10. No e2e spec for a below-5 school yet |
| 2 | Score never shown without cohort size, interval and methodology version in the same block | Prevents comparing noise (METRIC-ETHICS §3) | e2e catchment result; API contract tests | Done in the e2e suite; e2e job green on PR #10 |
| 3 | Every value shows source, as-of date and method | A value without provenance is the most damaging failure | e2e profile provenance table | Done in the e2e suite; e2e job green on PR #10 |
| 4 | Gmina with no catchment data shows "brak danych", never a guessed school | A guessed school is a false statement about a child's school | e2e no-data gmina | Done in the e2e suite; e2e job green on PR #10 |
| 5 | Methodology page: "nie jest oficjalnym rankingiem", reading guide, version, draft status | Users need to know what a score means before reading one | e2e methodology text | Text checked; reading guide still in review |
| 6 | Correction path for schools (`/dla-szkol`) | Required for a public data portal | e2e token on submit; admin flow after ADR-0008 | Token path checked; operator review is manual |
| 7 | Limited-confidence profile band (ADR-0009: cohort 5 to 8) | Keeps about 1 550 schools informative without a rank | Boundary tests at 4, 5, 8, 9 | Built: engine returns `limited` for 5 to 8 (no score, no rank), dashed badge and profile copy, excluded from ranking; boundary unit tests and `limited-band.spec.ts` need their first CI run |
| 8 | Phone-width layout with no horizontal scroll | Most searches happen on phones | Playwright viewport test | Partly. Visual collector at 360, 768 and 1366 px in `tests/e2e/visual` (run locally, not in CI). It asserts load and status only, not overflow. No CI gate yet |
| 9 | Accessibility pass: keyboard, contrast, screen-reader labels on all states | Required for a public service | axe check in Playwright | Partly. Contrast is measured in `docs/ux/DESIGN-SYSTEM.md`. The visual collector counts images without alt and unnamed form fields (local only). No axe test, not in CI |
| 10 | Catchment by street and number (`street_list` only; no polygon, no distance guess) | The wedge; the gmina decides the boundary | Gmina resolution linked; ADR-0010 | Working for the demo gmina; real gminas wait on data |
| 11 | Compare two to four schools | Useful after profiles are trusted | e2e three-column compare | Working; e2e job green on PR #10 |
| 12 | Ranking list (country and gmina) | Only after cohort rules and methodology are final | e2e descending scores and no sort control | Working; needs Phase B methodology sign-off |
| 13 | Preschool cost and availability (no quality score) | Phase D; a different data shape | Phase D scope | Pages exist: `/przedszkola` and `/przedszkola/[slug]`, practical facts only, no score. Gmina tuition tables are still OPEN in `docs/data/SOURCES.md` |
| 14 | Parent reviews and accounts | Phase E; DSA machinery required first | authservice wired; DSA terms | Pages and components exist: `/konto` (login, register, school claims), reviews and billing components. Login against authservice is NOT RUN in CI (`AUTH_BASE_URL` unset there) |
| 15 | AI explanation panel ("Wyjaśnij ten wynik"): delimited box, label "Wygenerowano przez AI — to nie jest ocena szkoły", structured facts beside it, link to `/metodologia`, graceful messages for 503, 502 and 429 | Reader harm if the text reads as a verdict; the facts must stay next to it (ADR-0011 B6) | Playwright: label and facts visible; 503 and 502 messages; no HTML from the model | Built (`web/app/src/components/explain-panel.tsx`). `ai-surface.spec.ts` runs in the CI e2e job. No live model call in CI (no key). Needs O1 (retention) before public launch |
| 16 | MCP connection page (`/mcp` docs) and the agent-facing wording of scores (score with interval, `n uczniów`, version) | Agents repeat numbers without context; the wording is the only guard they see | Protocol test on tool descriptions; review of the example conversation in `docs/mcp/MCP.md` | Written at PR 4; no public page yet; MCP not deployed |

## Rules for every screen

- Anti-goal in the UI: no per-person view and no sort that orders schools by an individual's data.
- No ascending sort on a composite score.
- Confidence and cohort size appear in the same component as the score.
- A school with 5 to 8 pupils who sat shows the limited profile: cohort size and subject averages, no score, no interval,
  no rank. A school with fewer than 5 shows `insufficient_cohort`: cohort size and reason, no value. Neither is ever
  shown as zero or as a rank (ADR-0009).
- The product never says "oficjalny ranking".
