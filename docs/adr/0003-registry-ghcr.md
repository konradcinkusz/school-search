# ADR-0003: Container registry GHCR

- **Status:** Accepted
- **Date:** 2026-10-11

## Context

The constitution's disagreement table settles on GHCR (portable, free at this scale). The Fly guide's pipeline
is written against `registry.fly.io`. Both are allowed; the choice is recorded once.

## Decision

Images are pushed to `ghcr.io/konradcinkusz/school-search-<service>` by CI. Deploys use
`flyctl deploy --image ghcr.io/konradcinkusz/school-search-<service>@<digest>`. Build once, deploy many.

Because the package is private, Fly must hold a pull credential. That credential is a Fly secret set from the
GitHub environment, never a value in the repository.

## Options considered

| Option | For | Against |
|---|---|---|
| GHCR | Portable across hosts; free at this scale; already where the source lives | Needs a pull credential as a Fly secret for private images |
| `registry.fly.io` | Fly authenticates natively; the guide's pipeline uses it | Ties the image to Fly; a host change means re-publishing |

## Consequences

Images survive a platform move. A GHCR token must be rotated. Switching to `registry.fly.io` is a one-line
change in the workflow and this ADR.
