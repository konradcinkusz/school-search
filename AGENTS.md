# AGENTS.md

How an automated agent (or a human using one) should work in this repository.
The standards are the reference, not this file: read them rather than guessing.

## Read first

1. The constitution: [`00-REFERENCE-ARCHITECTURE.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md) in `konradcinkusz/architecture-standards`.
2. The scaffold procedure this repository was created from: `docs/scaffold/INIT-GENERIC-TEMPLATE.md` in the same repository.
3. The guide for the layer you are touching, under `docs/guides/` in that repository.
4. [`docs/architecture/00-ARCHITECTURE.md`](docs/architecture/00-ARCHITECTURE.md): this repository's deviations from the constitution.
5. [`docs/adr/`](docs/adr/): decisions already taken. Do not reopen one silently.
6. The product brief, `docs/MASTER-PROMPT.md`.

If you cannot open a standard, stop and say so. Do not reconstruct it from memory.

## Hard rules

- **Agents never deploy.** Deploys come from a `v*` tag in CI. Do not run `flyctl deploy`, do not create Fly apps by hand.
- **No secrets in any file.** Use placeholders. Secrets come from `dotnet user-secrets`, the GitHub environment or Fly secrets.
- **Dependency automation is off** (ADR-0005). Do not switch on Dependabot version updates without the trigger in that ADR being met.
- **Do not fetch or scrape** `waszaedukacja.pl` or any competitor. Do not use the phrase "oficjalny ranking".
- **Pupil names** from any olympiad or other source are transient: never persisted, logged or placed in a fixture.
- **No teacher or pupil entity. No nationality or foreign-pupil data. No inferred people-heuristics** in any score.
- **Scoring must not reference billing, entitlement or user types** (architecture test). The kernel must not reference entity types and must stay under about 800 lines (CI check).
- **Every published value carries** `source`, `sourceUrl`, `asOf` and `method`. A score carries cohort size, interval and `methodologyVersion` in the same object.
- **Do not invent data facts, legal bases or numbers.** If a value is unknown, record it as OPEN in [`docs/data/SOURCES.md`](docs/data/SOURCES.md) with how to verify it.
- **No user accounts before Phase E** (ADR-0004). Do not add JWT validation, login UI or a user store.

## Stop and ask when

- a standard and the request conflict (record a deviation with its reason instead of deciding silently);
- an open unknown in `docs/data/SOURCES.md` would change the design;
- a new service, event bus, search engine or custom DI container is proposed (each needs a recorded ADR).

## Working in the tree

- Central package management: versions only in `Directory.Packages.props`. Projects reference packages without `Version`.
- Warnings are errors. Nullable is on.
- `Program.cs` is a manifest. Wiring lives in extension methods.
- Service-to-service HTTP uses the public URL, not `.internal`.
- Every new decision gets an ADR in `docs/adr/` (copy `0000-template.md`). Every acknowledged deviation gets a dated row in the deviation register.
- The README must stay true of the tree. Update it in the same change that adds or removes a project.
- Code, docs and commits are in English. Product UI copy is in Polish.

## Verification

Report each gate as run, passed, failed or NOT RUN. Never claim a build passed that was not run.
In environments without the .NET SDK, say "not compiled".
