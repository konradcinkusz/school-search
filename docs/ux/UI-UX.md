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

| Rank | Item | Why this rank | Gate |
|---|---|---|---|
| 1 | Every value shows source, as-of date and method | A value without provenance is the most damaging failure | Playwright assertion on profile pages (Phase C) |
| 2 | Score never shown without cohort size and confidence in the same block | Prevents comparing noise (METRIC-ETHICS §3) | Component test and API contract test |
| 3 | `insufficient_cohort` state rendered as an explicit message, never as 0 or blank | A zero reads as a bad school | Unit test on the state mapping |
| 4 | Methodology page with reading guide, version and draft status | Users need to know what a score means before reading one | Content review; `methodologyVersion` shown |
| 5 | Correction path for schools (`/dla-szkol`) | Required for a public data portal | Manual test; admin flow after ADR-0008 |
| 6 | Limited-confidence profile band (ADR-0009) | Keeps about 1 550 schools informative without a rank | Boundary tests at 4, 5, 8, 9 |
| 7 | Accessibility pass: keyboard, contrast, screen-reader labels on all states | Required for a public service | axe check in Playwright (Phase C) |
| 8 | Phone-width layout with no horizontal scroll | Most searches happen on phones | Playwright viewport test |
| 9 | Address to catchment school | The wedge; blocked on ADR-0010 and catchment data | ADR-0010 accepted; §3.7 verified |
| 10 | Compare two schools | Useful after profiles are trusted | Phase C |
| 11 | Ranking list | Only after cohort rules and methodology are final | Phase B methodology sign-off |
| 12 | Preschool cost and availability (no quality score) | Phase D; a different data shape | Phase D scope |
| 13 | Parent reviews and accounts | Phase E; DSA machinery required first | authservice wired; DSA terms |

## Rules for every screen

- Anti-goal in the UI: no per-person view and no sort that orders schools by an individual's data.
- No ascending sort on a composite score.
- Confidence and cohort size appear in the same component as the score.
- The product never says "oficjalny ranking".
