# ADR-0006: The MCP server is its own deployable

- **Status:** Accepted (design; implementation in a later phase)
- **Date:** 2026-10-11

## Context

The product brief asks for a hosted, read-only MCP server (streamable HTTP) with tools `find_schools`,
`get_school`, `get_catchment_school`, `compare_schools`, `get_ranking`, `get_methodology`, resources
`school://{rspo}` and `methodology://{version}`, and a guided prompt. It has its own API key bucket and
its own rate limits, and it must return the same score, confidence, cohort and `methodologyVersion` as the
web. This is not in the reference architecture's examples, so this ADR records the reasoning the constitution
asks for when a second service is added (P3, §4 non-goal "no second service without a reason").

The constitution's Fly guide and the brief both give the planned app as `school-search-mcp-dev`, with
`min_machines_running = 0` at launch.

## Decision

MCP is a separate deployable (`school-search-mcp-dev`, port 8080) that **owns no database**. It reads data only
by calling the catalog API over the public URL, the same path the web uses. It holds its own `MCP_API_KEY`
secret. It is a thin protocol adapter: transport, tool and resource mapping, and the suppression and labelling
rules applied through the API's own responses.

Its scale-to-zero trigger is the first real agent traffic (brief: "mcp-dev (0 at launch, trigger to pin = first
real agent traffic)").

## Options considered

| Option | For | Against |
|---|---|---|
| Separate deployable, no DB | Separate credential, rate limits and release cadence; cannot leak API internals | One more app to deploy and monitor |
| Route inside the API | No new app | Mixes protocol and trust levels; the API's scoring rules would have a second entry point to get wrong |
| Route inside the web BFF | Reuses the proxy | MCP clients are not browsers; ties the agent surface to the UI release |

## Consequences

The MCP service cannot see the database or scoring internals, so the suppression rule (no score below cohort
threshold) is enforced once, in the API, and inherited. A protocol-level test in CI is required before MCP is
exposed (brief, MCP section). Until that test exists, MCP is not deployed.
