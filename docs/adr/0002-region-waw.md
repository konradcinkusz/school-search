# ADR-0002: Fly.io primary region `waw`

- **Status:** Accepted
- **Date:** 2026-10-11

## Context

All Fly apps and the Postgres volume need a `primary_region`. The users are Polish parents. The data is
Polish public-sector data. The constitution's examples show both `waw` and `fra`, and neither is a default
(FLY-IO-DEPLOYMENT §8). A volume's region is expensive to change once it holds data.

## Decision

`primary_region = "waw"` for every app in `flyio/*.fly.toml` and for the Postgres app.

## Options considered

| Option | For | Against |
|---|---|---|
| `waw` (Warsaw) | Nearest to the users; Polish data residency is simplest to explain | Fewer machines and features in some Fly regions, to be checked at deploy time |
| `fra` (Frankfurt) | Mature region; common default in the estate | Further from users; no residency advantage |

## Consequences

Latency is lowest for the audience. The choice must be confirmed against Fly's region availability and
pricing before the first deploy, and recorded in `flyio/INFRASTRUCTURE-ANALYSIS.md`. Changing region later
means provisioning new volumes and migrating data. Re-open this ADR before any volume exists if that is wanted.
