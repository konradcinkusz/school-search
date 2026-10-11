# ADR-0001: Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-10-11

## Context

Decisions about this system are taken in conversations, pull requests and the scaffold. Without a record,
the next reader cannot tell a deliberate choice from a default that nobody examined. The constitution
(P14) asks that repositories record reasoning next to the code.

## Decision

Architecture decisions are recorded as numbered ADRs in `docs/adr/`, one decision per file, using
[`0000-template.md`](0000-template.md). A deviation from the constitution also gets a dated row in
[`docs/architecture/00-ARCHITECTURE.md`](../architecture/00-ARCHITECTURE.md). An ADR is never edited to
change its decision. A new ADR supersedes it.

## Options considered

| Option | For | Against |
|---|---|---|
| Numbered ADRs in the repository | Reviewable in the pull request; lives with the code | Needs discipline to keep current |
| Decisions in a wiki or chat | Quick to write | Drifts from the code; not reviewed |
| Nothing recorded | No overhead | Defaults look like requirements |

## Consequences

Every decision in the scaffold (region, registry, users, dependency automation, cohort threshold, geocoder)
has a file here. Pending decisions are recorded as Pending, not omitted.
