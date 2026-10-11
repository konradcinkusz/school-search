## What changes and why

<!-- One or two sentences. Link the phase (A-F) and the ticket or ADR if one exists. -->

## How it was verified

<!-- Mark what you ran. A gate you could not run is written as NOT RUN, never ticked. -->

- [ ] `dotnet build` clean, warnings as errors (NOT RUN if dotnet is absent locally; CI runs it)
- [ ] `dotnet test` green, and the tests added or changed assert something
- [ ] `pnpm` build and typecheck in `web/` (if the frontend changed)
- [ ] `scripts/scan-secrets.sh --all` clean
- [ ] Container health on `:8080` / `:3000` (if a Dockerfile or `fly.toml` changed)

## Standards

- [ ] No secret, token or real personal data in source, config, comments or fixtures
- [ ] Kernel (`src/SchoolSearch.ServiceDefaults/`) holds plumbing only: no entity types, no domain strings
- [ ] Scoring (`src/SchoolSearch.Api/Scoring/`) references no billing, entitlement or user types
- [ ] Every new optional integration degrades (P8) and is listed in `secrets.env.example` with its tier and a `degrades:` line
- [ ] Documentation changed with the code: README, `docs/`, `scripts/README.md` where relevant
- [ ] Any deviation from `architecture-standards` is recorded in `docs/architecture/00-ARCHITECTURE.md` with a date and a reason

## Scoring and data (only if this touches scores, methodology or source data)

- [ ] Every published value carries `source`, `sourceUrl`, `asOf`, `method`, and scores carry cohort size, interval and `methodologyVersion`
- [ ] Below the cohort threshold the result is `insufficient_cohort`, not null or zero
- [ ] No pupil names are persisted, logged or used as fixtures

## Notes for the reviewer

<!-- Anything you would want to be asked about. -->
