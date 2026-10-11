# Master delivery brief — `school-search` (condensed working copy)

> Condensed from the owner's master delivery prompt (v. 2026-10-11). Product is Polish; repo, code, docs, ADRs and commits are English.
> Standards source of truth: `konradcinkusz/architecture-standards` — read, never re-derive. Identity: `konradcinkusz/authservice` (external, never modified).

## Product
Polish primary-school (later preschool) search & ranking portal + hosted MCP server. Wedge: address -> catchment school (obwód) -> results/trend/confidence -> schools in reach.
Honest value-added proxy with confidence; moderated parent reviews (Phase E); preschools: cost & availability, NO score (Phase D).

## Target
Region `waw` · registry GHCR · users only from Phase E (no JWT wiring in A–D, ADR-0004) · Next.js BFF · no Azure.
Topology: school-search-postgres (no public listener, PGDATA subdir, --ha=false) · api-dev (catalogdb, min 1) · web-dev (0) · mcp-dev (0 at launch, trigger to pin = first real agent traffic) · import (one-shot machine) · authservice-dev (E, min 1) · reviews-dev (E).
Service-to-service HTTP uses public URL, not .internal.
Projects: AppHost, ServiceDefaults (kernel ≤ ~800 lines, no entity types), Contracts, Catalog.Data, Api (+ Scoring/ with no billing/user refs), Import, Mcp (owns no DB), web/app.

## Anti-goals (enforced by architecture, tested)
No ascending sort on composite · no teacher/pupil entity · scoring references no billing/entitlement (arch test) · scores suppressed below cohort threshold inside the engine · no nationality/cudzoziemcy data ever.

## Scoring (METRIC-ETHICS gate)
Exam component standardised per subject per year vs national mean, equal weights polski/matematyka/język obcy. Counter-metric = cohort coverage (CKE sat / SIO class VIII) BLENDED into the exam component (not a side badge); unit test: score falls when coverage drops while average rises.
Every score carries cohort size + interval + methodologyVersion in the same object; below threshold -> `insufficient_cohort` (not null/0). Threshold in one place, recorded as ADR (CKE floor 5, WaszaEdukacja 9; 1 550 schools between). No inferred people-heuristics at any weight. Methodology doc versioned.

## Data (CC0 SIO dataset 288; CKE/OKE; RSPO unverified)
SIO Szkoly_2025.xlsx 26 426×133; 14 224 primary rows; 13 885 real population; class VIII ≥5: 12 564; ≥9: 11 014. idTypOrgProw 141/146/150 = private individual: store type, NEVER expose name. Faith flag derived from governing body, never from name. Olympiad pupil names transient in memory only, never persisted/logged/fixtured. Czesne: value+currency+sourceUrl+asOf+method. Every published value carries source, sourceUrl, asOf, method.
Never fetch/scrape waszaedukacja.pl or any competitor. Never "oficjalny ranking". Schools get a correction path.
Unknowns to verify and record in docs/data/SOURCES.md (never guess): RSPO API fields/licence/coords; idTypOrgProw dictionary; CKE per-school export + join key; idPodmiot stability; geocoder licence; CKE sat-count availability; catchment availability per gmina.

## MCP (streamable HTTP, read-only v1)
Tools find_schools, get_school, get_catchment_school, compare_schools, get_ranking, get_methodology; resources school://{rspo}, methodology://{version}; guided prompt. Every response: score+confidence+cohort+methodologyVersion+asOf. Suppression survives protocol. UGC labelled userGenerated, separated. Protocol-level test in CI.

## Rate limiting / metrics
Anonymous IP bucket + free API key bucket; MCP holds its own key. Metrics never labelled by school id/address/query string.

## Legal
DSA machinery for reviews (Phase E): terms, notice mechanism, statement of reasons, contact point. UOKiK: state verification truthfully; paid promotion never suppresses negative reviews.

## Phases & gates
A scaffold+infra (INIT-GENERIC-TEMPLATE verbatim; gates: build, test, P8 zero-credential run, images /health :8080, standalone frontend + /api/config, scanner, workflow syntax) ·
B ingest+scoring (idempotent import, row counts vs §3.1, coverage test, no score without confidence, ~2100 insufficient_cohort, no pupil names) ·
C public search surface (SECURITY-REVIEW, Playwright in CI same commit, anti-goals as code absences, catchment for structured cities) ·
D preschools (no quality score) · E accounts+reviews (authservice, non-empty JWKS assertion, DSA) · F monetisation (arch test: scoring cannot reference billing).
A gate that could not run is reported as NOT RUN. Agents never deploy; deploys come from a tag in CI.

## Ground rules
Work only in this repo; standards/authservice read-only. No secrets in source. Stop-and-ask conditions: non-empty repo, standard conflict, §10 unknown changes design, about to invent data/legal basis/number. No second service, event bus, mesh, custom DI, search engine without recorded reason.
