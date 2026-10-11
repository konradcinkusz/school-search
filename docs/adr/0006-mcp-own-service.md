# ADR-0006: The MCP server is its own deployable

- **Status:** Accepted. Implemented in `src/SchoolSearch.Mcp` (PR 4), not compiled in the authoring environment and **not deployed**. Deployment waits for the protocol-level test in CI and the owner's go.
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

**What this decision now costs (recorded at PR 4):**

- **Two Fly apps to run and watch.** `school-search-mcp-dev` has its own health probe, its own `MCP_API_KEY`
  and its own rate limit (60 requests per minute per client IP on `/mcp`; `/health` and `/alive` are exempt).
- **Every MCP answer depends on the API being reachable.** The catalog call uses the public API URL with a 30 s
  timeout, which covers a cold start of the API. If the API is down, the tool returns an MCP tool error with the
  uniform message. It never returns a stale or invented value.
- **The MCP server is a second place where the JSON contract is consumed.** A change to the API's public contract
  (`SPEC-C` shapes: `ScoreBlock`, `SchoolListItem`, `SchoolProfile`) must be checked against the MCP tests in
  `tests/SchoolSearch.Mcp.Tests`. The MCP server passes the API JSON through verbatim, so a field the API drops
  is also dropped for agents.
- **Cold start is visible to agents.** `min_machines_running = 0` at launch means the first request after an idle
  period waits for a machine to start. The trigger to pin it to 1 is the first real agent traffic, recorded here
  so that the decision is made on data, not on guesswork.
- **Not verified.** The first CI run of the protocol test has not happened. The Anthropic-side and MCP SDK
  wire details are compiled only in CI (see `docs/mcp/MCP.md`, section "Status").

**Exit condition for "not deployed":** the MCP test job is green in CI on the merged commit, and the owner
asks for a tag-driven deploy. Agents never deploy.
