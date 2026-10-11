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

**Status: Phase A merged; Phase B in review.** Phase A holds the solution, the API skeleton, the web skeleton,
the Fly.io definitions and the CI workflows. Phase B adds the import job and the scoring engine (ingest and
scoring, under review). No user accounts exist yet. No score is shown on any public surface: that is Phase C.

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
| `src/SchoolSearch.Import` | Import console app (Phase B, in review): `sio`, `cke`, `baseline`, `geo` and `check-schema` commands. Writes `catalogdb` directly (ADR-0013). Runs as a one-shot job, not a service. |
| `tests/SchoolSearch.Import.Tests` | Import tests, xUnit with the InMemory database and synthetic inputs built in the test. |
| `web/app` | Next.js product surface, reading the API through a server-side proxy. Listens on 3000. |
| `tests/SchoolSearch.Api.Tests` | Unit and integration tests, InMemory database. |
| `tests/SchoolSearch.Architecture.Tests` | Architecture rules: the kernel holds no entity type; `Scoring` does not reference billing or user types. |
| `tests/e2e` | Playwright journey, run in CI against the built web and API. |
| `flyio/` | Fly.io app definitions and the infrastructure analysis. |
| `docs/` | Architecture, ADRs, UX backlog, data sources, methodology, diagrams, overview paper. |

The MCP server is a later phase and is not in this tree yet. The import job is a second deployable of the
catalog context (ADR-0007).

## Documentation

- [`AGENTS.md`](AGENTS.md): how automated and human contributors work in this repository.
- [`docs/architecture/00-ARCHITECTURE.md`](docs/architecture/00-ARCHITECTURE.md): this repository measured against the reference architecture, and its deviation register.
- [`docs/adr/`](docs/adr/): architecture decision records.
- [`docs/ux/UI-UX.md`](docs/ux/UI-UX.md): screens, flows and the ranked backlog.
- [`docs/data/SOURCES.md`](docs/data/SOURCES.md): data sources and the open questions about them.

## Licence

MIT. See [`LICENSE`](LICENSE). Copyright Konrad Cinkusz.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
