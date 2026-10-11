# ADR-0017: The AI agent lives in the MCP service, and the batch extractors run on the operator's machine

- **Status:** Accepted for Phase G. The placement decision is recorded here, closing decision D1 of `docs/ai/AI-FEATURES.md` section 12.
- **Date:** 2026-10-11
- **Deciders:** owner (Konrad Cinkusz). Phase G brief, `SPEC-G`.

## Context

Phase G adds five AI features and one agent to a product that already has one AI call (feature 1, the explanation, in
the catalog Api, ADR-0011) and two more in other services (review triage in Reviews, the profile-copy assistant in
Billing, ADR-0016). The new work has two shapes:

- **Online, interactive:** natural-language search to filters (feature 2), correction triage for a moderator
  (feature 5), methodology questions (feature 7), and the "Doradca wyboru szkoły" agent. The agent calls tools over
  several turns, and its tools are the read-only operations that already exist (`find_schools`, `get_school`,
  `compare_schools`, `get_ranking`, `get_methodology`, `find_preschools`, `get_preschool`, `get_school_reviews`).
- **Batch, offline:** the catchment-resolution extractor (feature 3) and the preschool tuition extractor (feature 6). Each
  reads a document that a human saved, asks the model for candidate rows, and writes a candidate file plus a review file.
  A human verifies the file and loads it through the existing import path (B8).

The question was where each part runs and which service holds the credential. The constraints are ADR-0006 (the MCP
service owns no database and is a separate deployable for a reason), P10 (a credential-bearing dependency with an
availability risk becomes a service), ADR-0011 (one key, one SDK boundary per deployable, no shared library), and the
AI-FEATURES rule that no model output is published without a human decision.

## Decision

1. **The agent lives in `src/SchoolSearch.Mcp/Agent`, inside the existing MCP service.** The MCP service already
   owns the read-only tool surface and the catalog and reviews clients. The agent calls the same tool classes in process,
   so there is no second copy of a tool and no HTTP call from the service to itself. The MCP project already references
   the Anthropic package. No new deployable is created. `POST /agent/chat` is plain HTTP, not MCP, so the MCP protocol
   surface is unchanged.
   - **Accepted cost:** the MCP service now holds `ANTHROPIC_API_KEY`, a credential-bearing dependency on a service that
     is reachable without authentication in v1. The controls are the per-IP rate limit, the per-request limits (6 model
     turns, 8 tool calls, a 20 s deadline and a token budget), the process-wide daily budget `AI_DAILY_TOKEN_BUDGET`, and
     the kill switch `AI_ENABLED=false`.
   - **Exit condition:** the agent needs an availability target or a scaling profile that differs from the MCP read
     surface, or a second AI caller needs the same tools. Then move the loop to its own service, as P10 requires.
2. **The online features stay with the data they read.** Feature 2 (search intent) and feature 7 (methodology answers)
   are in the catalog Api, `src/SchoolSearch.Api/Ai`, because they depend on the Api's search filter contract and on the
   methodology document. Feature 5 (correction triage) is in the Api too, because corrections are stored there and it is
   behind the Api's admin gate. Feature 1 is unchanged (ADR-0011). Nothing in these features writes to a database.
3. **The batch extractors live in `src/SchoolSearch.Import` and run only on the operator's machine, with the operator's
   key.** They are console commands (`catchment-extract`, `preschool-extract`). They read a saved file, never fetch a URL,
   and write a candidate CSV and a `.review.md`. They never open a `DbContext`. Their output is loaded through the existing
   `catchment` and `preschool-facts` paths after a human has signed it off. No flag approves output in bulk.
   - The Import image is still built and pushed by `flyio.yml` (it is a one-shot job), but **no Fly app runs it**, and
     `ANTHROPIC_API_KEY` is never pushed to Fly for Import. A key on Fly would be a second holder for a job that needs
     none.
   - Architecture rule: no `*.Ai`, `*.Extract` namespace of Import depends on `SchoolSearch.Catalog.Data` or on Entity
     Framework (`tests/SchoolSearch.Architecture.Tests/AiBoundaryTests.cs`).
4. **The Anthropic SDK is confined to files named as the SDK boundary.** Only `Anthropic*Client.cs` or `*Llm.cs` may
   reference the `Anthropic` namespace, in any deployable. The rest of the code talks to an interface, so tests use a fake
   client and an SDK change is a one-file fix. `AiBoundaryTests` enforces the naming rule by scanning the source.
   - **Deviation from the SPEC wording:** SPEC-G says "exactly ONE file per service". The Api now hosts four AI features
     (explain, search intent, methodology answers, correction triage) and each has its own client file. A count of one
     cannot hold, so the rule is the naming boundary, not a count. Recorded in the deviation register.
5. **Every AI kill switch is off in the deployed configuration.** `AI_ENABLED` is `false` in `flyio/api.fly.toml`,
   `flyio/reviews.fly.toml`, `flyio/billing.fly.toml` and `flyio/mcp.fly.toml`. `flyio.yml` pushes `ANTHROPIC_API_KEY` to
   the four services that need it, when the GitHub secret exists. A key therefore enables nothing. Turning a feature on is
   a reviewed change to a `[env]` line, made after that feature's eval gate (`docs/ai/AI-EVALS.md`).
6. **Architecture tests cover the placement.** The MCP project joins the architecture test project (conditionally, like
   Reviews and Billing). The identity and monetisation rules now include the MCP assembly: it validates no token, mints
   none, and has no path to billing.

## Options considered

| Option | For | Against |
|---|---|---|
| **Agent in the MCP service (chosen)** | Owns the tool surface already. Reuses the tool classes in process. No new deployable. The SDK is already referenced there. | The MCP service becomes a credential holder and is anonymous in v1. Its cost controls must be correct. |
| Agent in the catalog Api | The Api already holds the key for feature 1 | Puts a tool loop and a second, larger cost surface beside the scoring engine. The Api would call its own endpoints or duplicate the tool classes. |
| A new `agent` service | Clean boundary, its own key and limits | A third credential-bearing service for one endpoint. ADR-0006 and P10 require a recorded reason, and the MCP service already is one. |
| Agent in the web BFF | No new service | The BFF is a proxy with no data access. Tools would be HTTP calls to the catalog and Reviews, and the key would sit in the front end process. Rejected. |
| Extractors deployed as a Fly job | Scheduled, automatic | The model output would reach the database without a human, which breaks B8. The key would live on Fly for a job that needs none. Rejected. |

## Consequences

- `POST /agent/chat` in the MCP service is the only agent route. The web BFF route `/api/agent` forwards to it with the
  visitor's address and the shared secret, and `AGENT_BASE_URL` is set for the web app in `flyio/web.fly.toml`, the AppHost
  and the CI e2e job (port 8084).
- The extractors are not deployed. Their CLI runs from a trusted machine with `ANTHROPIC_API_KEY` in the environment, and
  the runbook (`docs/runbooks/AI-OPERATIONS.md`) says how to verify the candidate file before import.
- Costs are per process and reset on restart. This is the same soft cap as ADR-0011 item 11, with the same exit condition.
- Pending: the data-protection review of the agent (what the model sees from reviews) and the eval set for each feature.
  Neither is closed by this ADR. Both are listed in `docs/security/SECURITY-REVIEW-PHASE-G.md` and `docs/ai/AI-EVALS.md`.

## Deviations (recorded in `docs/architecture/00-ARCHITECTURE.md`)

- The one-file-per-service rule of the SPEC is replaced by the naming rule in decision 4.
- The Api's AI clients are one file per feature, not one shared client. Each copies the proven request shape of
  `AnthropicExplainClient.cs`. This is the accepted duplication recorded for Billing in ADR-0016.
