# ADR-0007: The Catalog context has two deployables, the API and the import job

- **Status:** Accepted (design; Import arrives in Phase B)
- **Date:** 2026-10-11

## Context

The catalog API serves reads. The import job loads the public SIO dataset (idempotent, about 26 426 rows by 133
columns in the 2025 file, per the brief) and the later scoring runs need the same entity model, the same
`CatalogDbContext` and the same migrations. The constitution says one service owns one database (P3) and
forbids shared ORM entities across services (§4). This ADR records why the import job is a second deployable
of the same context rather than a second service.

## Decision

- `src/SchoolSearch.Import` (Phase B) is a one-shot deployable that **references** `src/SchoolSearch.Catalog.Data`.
  It runs as an ephemeral Fly machine (`machine run`), not a long-lived app.
- `Catalog.Data` is the single owner of the catalog schema. Migrations are applied by the API's hosted service, and
  the import uses the same provider switch. Import writes; the API reads and is the only public surface.
- This is recorded as a deviation (see the register in `00-ARCHITECTURE.md`), with the exit condition: if Import
  becomes its own bounded context with its own schema, split `Catalog.Data`.

## Options considered

| Option | For | Against |
|---|---|---|
| Import in the API process | One deployable | Long-running import competes with serving; violates P1/P9 shape |
| Import as separate service with its own DB | Strict P3 | Duplicates the school entity; two sources of truth for one dataset |
| Import as a second deployable of the same context, shared Catalog.Data | One schema owner; ephemeral job | A shared DbContext is a recorded deviation |

## Consequences

Changes to `Catalog.Data` invalidate both images, and the change-detection rule in CI must reflect that
(a change to a shared project rebuilds every image that compiles it). The architecture test must allow
Import to reference Catalog.Data and forbid Api or Scoring from referencing Import.
