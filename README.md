<a name="readme-top"></a>

# school-search

[![Ask Me Anything](https://flat.badgen.net/static/Ask%20me/anything?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz "Ask me anything")
[![License](https://flat.badgen.net/github/license/konradcinkusz/school-search?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/school-search/licenses "GitHub license")
[![Maintained](https://flat.badgen.net/static/Maintained/yes?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/school-search/commits "Maintained")
[![Branches](https://flat.badgen.net/github/branches/konradcinkusz/school-search?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/school-search/branches "Branches")
[![Commits](https://flat.badgen.net/github/commits/konradcinkusz/school-search?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/school-search/commits "Commits")
[![Issues](https://flat.badgen.net/github/issues/konradcinkusz/school-search?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/school-search/issues "Issues")
[![Pull requests](https://flat.badgen.net/github/prs/konradcinkusz/school-search?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/school-search/pulls "Pull requests")
[![CI](https://github.com/konradcinkusz/school-search/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/school-search/actions/workflows/ci.yml "CI")
[![Secret scan](https://github.com/konradcinkusz/school-search/actions/workflows/secret-scan.yml/badge.svg)](https://github.com/konradcinkusz/school-search/actions/workflows/secret-scan.yml "Secret scan")

A search and ranking portal for Polish primary schools (szkoły podstawowe), with a hosted,
read-only MCP server planned for the same data. The wedge is an address: it finds the
catchment school (obwód) for that address, then shows the school's results, their trend, and
how much confidence the data supports, alongside the other schools a family can realistically reach.

**Status: Phase A merged; Phase B in review; Phase E (accounts and reviews) in review.** Phase A holds the solution, the API
skeleton, the web skeleton, the Fly.io definitions and the CI workflows. Phase B adds the import job and the scoring engine
(ingest and scoring, under review). Phase E adds sign-in through authservice (the only identity provider), moderated
reviews, the DSA notice and statement-of-reasons machinery, and the Reviews service. Nothing of Phase E is deployed, and
login against a real authservice has not run. No score is shown on any public surface: that is Phase C.
Phase F (monetisation, in review) adds the Billing service: a sponsored school profile and parent enquiries, sold without any
effect on scores, rankings or reviews. No payment provider is chosen, so nothing is sold yet (ADR-0016, `docs/runbooks/BILLING.md`).

**PR 4 (MCP and AI explanation), not deployed.** A read-only MCP server (`src/SchoolSearch.Mcp`) and one AI feature
("Wyjaśnij ten wynik", a plain-Polish explanation of a school's score, built from the API's own data and labelled as AI)
are written. Neither is compiled in the authoring environment, neither is deployed, and the MCP protocol test has not run in CI.
The planned MCP URL is `https://school-search-mcp-dev.fly.dev/mcp` and it does not resolve yet. See
[`docs/mcp/MCP.md`](docs/mcp/MCP.md) for connecting an agent and [`docs/adr/0011-anthropic-api-usage-boundaries.md`](docs/adr/0011-anthropic-api-usage-boundaries.md) for the AI boundaries.

## What this is not

- Not an official ranking. The portal does not present itself as one, and it never uses the phrase.
- Not a judgement about individual teachers or pupils. There is no teacher or pupil entity and no per-person view.
- Not a place for nationality or foreign-pupil data. That data is never collected.
- Scores are suppressed below a cohort threshold. A school with too few pupils gets an explicit
  `insufficient_cohort` state, not a number.
- Each published value carries its source, source URL, as-of date and method.

The metric rules are in [`docs/methodology/RANKING-METHODOLOGY.md`](docs/methodology/RANKING-METHODOLOGY.md)
(version 1.0.0) and the standards they follow in [`docs/architecture/00-ARCHITECTURE.md`](docs/architecture/00-ARCHITECTURE.md).

## Run it

Prerequisites: the .NET 10 SDK, Node.js with pnpm (for the web app), and optionally a container
runtime (Aspire starts Postgres as a container when `DATABASE_PROVIDER=postgres`).

```bash
./scripts/setup.sh && dotnet run --project src/SchoolSearch.AppHost
```

On Windows, use `scripts\setup.ps1` in place of `setup.sh`. No cloud credential is needed to start.
Optional integrations are listed as `degraded` or `disabled` in `GET /health` and in the startup
banner. Every variable and what degrades without it is in [`scripts/README.md`](scripts/README.md)
and [`secrets.env.example`](secrets.env.example).

The web app is in `web/` (a pnpm workspace). Run it with `pnpm -C web/app dev` once the API is up.

Run the tests with `dotnet test SchoolSearch.sln`. The end-to-end journey is in `tests/e2e`
(Playwright).

## Projects

| Path | What it is |
|---|---|
| `src/SchoolSearch.AppHost` | Aspire composition root. Development only; not the production topology. |
| `src/SchoolSearch.ServiceDefaults` | Shared plumbing kernel: telemetry, health, resilience, CORS, database provider. No entity types. Size is checked in CI. |
| `src/SchoolSearch.Contracts` | DTOs that cross a service boundary. |
| `src/SchoolSearch.Catalog.Data` | Catalog persistence: `CatalogDbContext` and the school entity. Owns the `catalogdb` schema. |
| `src/SchoolSearch.Api` | The catalog API (`/api/v1/schools`, `/api/v1/meta`), `/health` and `/alive`. Listens on 8080. |
| `src/SchoolSearch.Api/Scoring` | The scoring engine and ranking builder (Phase B, in review). Pure functions, no I/O. |
| `src/SchoolSearch.Mcp` | Read-only MCP server over streamable HTTP (PR 4, not deployed). Six tools, two resources, one guided prompt. Calls the catalog API over its public URL and owns no database (ADR-0006). Rate limit 60 requests per minute per IP on `/mcp`. Hosts the Phase G agent "Doradca wyboru szkoły" at `POST /agent/chat` (`src/SchoolSearch.Mcp/Agent`, ADR-0017), off until enabled. |
| `src/SchoolSearch.Reviews` | Reviews service (Phase E, in review): user-generated opinions, DSA notices, statements of reasons, moderation. Owns `reviewsdb`. Validates authservice tokens; mints none. Listens on 8080. |
| `src/SchoolSearch.Reviews/Dsa` | DSA machinery (art. 11, 12, 14, 16, 17, 20): notices, statements of reasons, complaints, contact point, transparency counts (Phase E). See `docs/legal/DSA.md`. |
| `tests/SchoolSearch.Reviews.Tests` | Reviews service tests, InMemory database and a test authentication scheme (Phase E, not yet run in CI). |
| `src/SchoolSearch.Billing` | Billing service (Phase F, in review): sponsored school profiles, parent enquiries and subscription entitlements. Owns `billingdb`. Provider-neutral webhook; no provider chosen (ADR-0016). Validates authservice tokens; mints none. Listens on 8080. |
| `tests/SchoolSearch.Billing.Tests` | Billing service tests, InMemory database and a test authentication scheme (Phase F, not yet run in CI). |
| `tests/SchoolSearch.Mcp.Tests` | Protocol-level tests of the MCP server, raw JSON-RPC over HTTP against a stub catalog (PR 4, not yet run in CI). |
| `src/SchoolSearch.Api/Ai` | AI in the catalog API: the explanation `POST /api/v1/schools/{slug}/explain` (PR 4), and Phase G's search intent, methodology answers and correction triage. Each needs `ANTHROPIC_API_KEY` and its `AI_ENABLED` switch (off in the deployed config). Without them it returns 503 and the rest of the product is unchanged (ADR-0011, ADR-0017). |
| `src/SchoolSearch.Import/Ai`, `Catchment/Extract`, `Preschools/Extract` | Phase G batch extractors (catchment resolutions and preschool tuition tables). Run only by an operator with their own key; they write candidate files for human sign-off and never touch the database (ADR-0017). |
| `src/SchoolSearch.Import` | Import console app (Phase B, in review): `sio`, `cke`, `baseline`, `geo` and `check-schema` commands. Writes `catalogdb` directly (ADR-0013). Runs as a one-shot job, not a service. |
| `tests/SchoolSearch.Import.Tests` | Import tests, xUnit with the InMemory database and synthetic inputs built in the test. |
| `web/app` | Next.js product surface, reading the API through a server-side proxy. Listens on 3000. |
| `tests/SchoolSearch.Api.Tests` | Unit and integration tests, InMemory database. |
| `tests/SchoolSearch.Architecture.Tests` | Architecture rules: the kernel holds no entity type; `Scoring` does not reference billing or user types; scores, rankings and reviews have no path to billing (`MonetisationBoundaryTests.cs`); the Anthropic SDK sits only behind `Anthropic*Client`/`*Llm` files and AI code never reaches scoring, the catalog data or the database from the extractors (`AiBoundaryTests.cs`). |
| `tests/e2e` | Playwright journey, run in CI against the built web and API. |
| `flyio/` | Fly.io app definitions (postgres, authservice, reviews, billing, api, mcp, web) and the secret inventory (`SECRETS.md`). |
| `docs/` | Architecture, ADRs, UX backlog, data sources, methodology, diagrams, overview paper, the AI feature record (`docs/ai/`), its operations runbook (`docs/runbooks/AI-OPERATIONS.md`) and security reviews. |

The MCP server is in this tree (PR 4) but is not deployed. The import job is a second deployable of the
catalog context (ADR-0007).

## Documentation

- [`AGENTS.md`](AGENTS.md): how automated and human contributors work in this repository.
- [`docs/architecture/00-ARCHITECTURE.md`](docs/architecture/00-ARCHITECTURE.md): this repository measured against the reference architecture, and its deviation register.
- [`docs/adr/`](docs/adr/): architecture decision records.
- [`docs/ux/UI-UX.md`](docs/ux/UI-UX.md): screens, flows and the ranked backlog.
- [`docs/mcp/MCP.md`](docs/mcp/MCP.md): connecting an agent to the MCP server, tools, rate limits, privacy.
- [`docs/ai/AI-FEATURES.md`](docs/ai/AI-FEATURES.md): the seven proposed AI features, with the status of each.
- [`docs/proposals/MCP-SURFACE.md`](docs/proposals/MCP-SURFACE.md): a draft for the standards repository on exposing metrics through MCP. Not filed.
- [`docs/data/SOURCES.md`](docs/data/SOURCES.md): data sources and the open questions about them.
- [`docs/runbooks/AUTHSERVICE.md`](docs/runbooks/AUTHSERVICE.md): the identity provider: keys, databases, deploy, rotation, post-deploy assertions.
- [`docs/legal/DSA.md`](docs/legal/DSA.md): the DSA mapping for reviews, what is not implemented, and what the owner must supply. Terms and privacy notice drafts: `docs/legal/`.
- [`docs/security/SECURITY-REVIEW-PHASE-E.md`](docs/security/SECURITY-REVIEW-PHASE-E.md): the Phase E threat model and verification record.
- [`docs/security/SECURITY-REVIEW-PHASE-F.md`](docs/security/SECURITY-REVIEW-PHASE-F.md): the Phase F threat model (webhook, claims, enquiries, sponsored text) and what was not run.
- [`docs/runbooks/BILLING.md`](docs/runbooks/BILLING.md): the Billing service: the provider decision, database, secrets, deploy, webhook go-live checks, rollback.
- [`docs/legal/SALES-TERMS-DRAFT.pl.md`](docs/legal/SALES-TERMS-DRAFT.pl.md) and [`docs/legal/ENQUIRY-PRIVACY-DRAFT.pl.md`](docs/legal/ENQUIRY-PRIVACY-DRAFT.pl.md): Phase F sales terms and enquiry privacy notice, drafts for legal review with OPEN items.

## Licence

MIT. See [`LICENSE`](LICENSE). Copyright Konrad Cinkusz.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
