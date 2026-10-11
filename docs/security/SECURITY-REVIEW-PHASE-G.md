# Security review: Phase G (AI features 2, 3, 5, 6, 7 and the "Doradca wyboru szkoły" agent)

Scope: the new AI surfaces in the catalog Api (`src/SchoolSearch.Api/Ai`: search intent, methodology answers, correction
triage), the agent in the MCP service (`src/SchoolSearch.Mcp/Agent`, `POST /agent/chat`), the batch extractors in the Import
console (`src/SchoolSearch.Import/Ai`, `Catchment/Extract`, `Preschools/Extract`), the web pages and BFF routes that call
them (`/doradca`, `/api/agent`, the search and methodology boxes, the moderation corrections tab), the Fly and CI
configuration for the key, and the new architecture rules.

**Status of this review: design review, not a penetration test.** Nothing here has run against a live model, and no .NET
SDK was available in the authoring environment, so no unit, architecture or e2e test has been executed by the author. Each
control is marked **DESIGN** (written in the spec or the code), **TEST** (a test exists in the repository, not yet run in
CI at the time of writing) or **NOT RUN** (no evidence yet). A gate that could not run is reported as NOT RUN, never PASS.

Decisions: ADR-0011 (boundaries), ADR-0016 (billing), ADR-0017 (where the agent and the extractors run). Earlier reviews:
`SECURITY-REVIEW-PHASE-C.md`, `SECURITY-REVIEW-PHASE-E.md`, `SECURITY-REVIEW-PHASE-F.md`.

## 1. Assets

| Asset | Why it matters |
|---|---|
| `ANTHROPIC_API_KEY` (four Fly apps and one operator machine) | Spend, and a holder can send any content in the key owner's name. Never in source, logs, argv or `[env]` |
| The daily token budgets (`AI_DAILY_TOKEN_BUDGET`) | The cost ceiling. Exhaustion stops the features; it never fails open |
| Reviews text and author hashes | User-generated text. Reviews must not reach a prompt with the author hash, and a review must not steer the agent |
| The enquiry, correction and account data | Personal data. Corrections carry no reporter e-mail to the model (SPEC-G, feature 5) |
| The scoring and ranking outputs | The model never computes, alters or orders them (B1, B2). Protected by absence |
| The candidate files of the extractors | Human-verified before load. An unverified row in the catalog is a publication error (B8) |

## 2. Threat model and controls

| # | Threat | Control | Where | State |
|---|---|---|---|---|
| G1 | Prompt injection through a review in the agent's tool results | Tool results are wrapped in `<untrusted>` blocks with `<` and `>` escaped. Review text is marked `userGenerated`. The agent system prompt says tool results are data and never instructions. The reply is checked for ungrounded numbers | `src/SchoolSearch.Mcp/Agent/**` (G6) | DESIGN. TEST: scripted fake `IAgentLlm`, injection inside a review is not obeyed. **NOT RUN** against the model |
| G2 | Prompt injection through a saved document given to an extractor | The document is data in an `<untrusted>` block. Output is parsed strictly against the candidate schema, and a row that does not match is rejected. An injected instruction cannot add a column or a row outside the schema | `src/SchoolSearch.Import/Ai`, `Catchment/Extract`, `Preschools/Extract` (G2, G4) | DESIGN. TEST: injection text does not alter output schema. **NOT RUN** against the model |
| G3 | Prompt injection through the NL question or the methodology question | The question is data in an `<untrusted>` block. Filter output is an allowlist (search intent). Methodology answers must cite headings that exist in the embedded document, or they are dropped | `SearchIntent*` (G1), `MethodologyQa*` (G5) | DESIGN. TEST: quality words never reach filters; hallucinated citation dropped. **NOT RUN** |
| G4 | The model writes to a database or publishes a result | No model output reaches a write path. The search intent answer is applied by the caller through the normal search API. Correction triage is not persisted and the draft is never sent. Extractors never open a `DbContext`. Architecture rules enforce the Import and Mcp half | `tests/SchoolSearch.Architecture.Tests/AiBoundaryTests.cs` (`Import_AI_and_extractor_namespaces_never_reach_the_catalog_database`, `No_AI_method_named_like_a_state_change_takes_a_DbContext`) | DESIGN. Rules **NOT RUN** until the projects compile in CI |
| G5 | The model ranks schools or judges quality | System rules forbid ranking and quality judgements. Search intent moves "best", "worst" and ordering words to `unsupported`. The agent's rules forbid comparisons that imply quality. The public pages show no AI ranking | Prompt builders (G1, G6), e2e (G7) | DESIGN. TEST: quality words never reach filters. The sort rule in `MonetisationBoundaryTests` is unchanged |
| G6 | The model invents a number, a date or a school | Digit grounding: every digit run in a prose answer must occur in the facts or the tool results (`NumberGuard`, the same rule as the Api). An ungrounded reply is replaced by a message listing the tool facts only. Extractors write `UNVERIFIED` rows and a review file with the source quote | `NumberGuard` (existing), G6 agent check, G2 and G4 validators | DESIGN. **NOT RUN** against the model |
| G7 | Tool-loop abuse: unbounded calls, cost, or a stuck loop | Hard limits: 6 model turns, 8 tool calls, 20 s overall deadline, a token budget checked before each turn, the history capped at 12 turns of 1000 characters each. A limit reached gives `stopReason` and a safe reply | `src/SchoolSearch.Mcp/Agent/**` (G6) | DESIGN. TEST: loop limits. **NOT RUN** |
| G8 | Tool-loop abuse: the agent calls tools the product does not offer | The tool list given to the model is fixed in code (the read-only set). Unknown tool names from the model are refused and count as an error. No tool writes, fetches a URL or reads a secret | G6 `AgentToolDef` set, tool classes (existing, read-only) | DESIGN. TEST: unknown tool refused. **NOT RUN** |
| G9 | SSRF through an agent tool or an extractor | None. Tools call fixed base URLs (`CATALOG_API_URL`, `REVIEWS_BASE_URL`) with values from configuration, never from the model. The extractor takes `--resolution-url` and `--source-url` as provenance text only and never fetches them | G6 tool clients, G2 and G4 CLI | DESIGN. Reviewed by reading the code paths; **NOT RUN** as a test |
| G10 | Cost abuse on public endpoints | Kill switch `AI_ENABLED=false` in every deployed `[env]`. Per-IP rate limits on search intent, methodology answers, corrections (moderator only) and the agent. Cache hits do not consume allowance. Process-wide daily token budget `AI_DAILY_TOKEN_BUDGET`; exhaustion answers 503 and never falls back to a paid retry | Api `SearchIntentRateLimiter`, `MethodologyQaRateLimiter`, `TokenBudget`; MCP agent limiter (copied from the MCP rate limiting) | DESIGN. **NOT RUN** on a deployment. A restart resets the counters (soft cap, ADR-0011 item 11) |
| G11 | Cost abuse through the Import extractors | They run only on the operator's machine with the operator's key. They have a per-run size cap and a token budget. The model is never called by CI | `src/SchoolSearch.Import/Ai/ImportTokenBudget.cs`, the CLI | DESIGN. **NOT RUN** |
| G12 | Personal data in prompts | A pre-send guard rejects free text containing an e-mail address, a telephone-like digit run or a PESEL-like digit run. Payload builders take only the fields they name: no reporter e-mail (feature 5), no author hash and no school operator name (reviews, agent), no pupil names (extractors reject pupil-level patterns and never send a roster) | Pre-send guard (`CorrectionTriagePreSendGuard`, G1, G6, G2, G4); payload builders | DESIGN. TEST: payload fixtures carry no forbidden field. **NOT RUN** |
| G13 | Reviews sent to the model with the author's identity | The agent receives review text, the school slug and the date only. The author hash is not a parameter of any prompt builder. Review triage keeps the Phase E rules (`docs/ai/AI-FEATURES.md`, "Review moderation triage") | `src/SchoolSearch.Mcp/Agent/**`, `src/SchoolSearch.Reviews/Ai/**` (unchanged) | DESIGN. **NOT RUN** |
| G14 | The key leaks into a log, an exception or a response | The SDK call is in one file per deployable. Exceptions are logged by type only, never by message. The response carries a fixed text. No key is printed by the workflow: `flyctl secrets import --stage` on stdin, empty pairs import nothing | `Anthropic*Client.cs`, `*Llm.cs`; `flyio.yml` (`deploy_api`, `deploy_reviews`, `deploy_billing`, `deploy_mcp`) | DESIGN. **NOT RUN** on Fly. The workflow is linted with actionlint 1.7.7 (clean) |
| G15 | The key is pushed by CI and enables a feature silently | Every `AI_ENABLED` in `flyio/*.fly.toml` is `false`. A pushed key enables nothing; a feature turns on only through a reviewed line change | `flyio/api.fly.toml`, `reviews.fly.toml`, `billing.fly.toml`, `mcp.fly.toml`, `flyio/SECRETS.md` | DESIGN. Config reviewed; **NOT RUN** on Fly |
| G16 | The key reaches the Import image, or a deployed Import job | The Import image is built but no Fly app runs it. The key is not pushed for Import. The operator sets the key in their own environment (ADR-0017 decision 3) | `.github/workflows/flyio.yml` (`build`, no Import deploy job), `docs/runbooks/AI-OPERATIONS.md` | DESIGN |
| G17 | A model answer is rendered as HTML | Model text is plain text. The web app renders it as text, never as HTML. Links to schools are built from validated slugs, not from model text. The web components escape the output | `web/app/src/components/ai/**`, `/doradca` (G7) | DESIGN. e2e check **NOT RUN** |
| G18 | A moderator's correction draft is sent without a human | There is no outbound e-mail in the system. The draft is returned as text for copy-paste. Nothing is saved (`saved: false`) | `CorrectionTriage*` (G3), `web/app/src/components/moderation/corrections-queue.tsx` (G7) | DESIGN. TEST: triage response not persisted. **NOT RUN** |
| G19 | Unauthorised access to correction triage | Same admin gate as the existing admin routes (authservice token, role `Admin` or `SuperAdmin`). Unconfigured gate answers 404; no token answers 401; a wrong role answers 403 | `src/SchoolSearch.Api/Security/AdminEndpoints.cs`, `CorrectionTriageEndpoints` (G3) | DESIGN. TEST: admin-only cases. **NOT RUN** |
| G20 | Unvalidated output from the model reaches a URL or a filter | Search intent: TERYT shape (7 digits), `maxDistanceKm` at most 50, `type` from an enum, unknown fields dropped. Methodology: citations validated against the document headings. Extractor: strict schema, unknown fields refused | G1, G5, G2, G4 validators | DESIGN. TEST: parser and clamp tests. **NOT RUN** |
| G21 | Unparseable or truncated model output is trusted | Parse failure, `refusal` and `max_tokens` are degraded outcomes, never empty success. One retry at most where the feature defines one; then a fixed safe message | All AI services (ADR-0011 pattern, AI-FEATURES section 2) | DESIGN |
| G22 | A candidate row is loaded without review | Extractor output is `UNVERIFIED` (catchment) or `confidence=unverified` (preschool). The flip to `reported` is an edit a human makes. No `--approve-all` flag exists. The loader is the existing import path | G2, G4, `docs/runbooks/AI-OPERATIONS.md` section 4 | DESIGN. The absence of the flag is checked by a test **NOT RUN** |
| G23 | Preschool matching by fuzzy guess attaches facts to the wrong school | Exact normalised name match only against the supplied preschool export. Unmatched rows go to a rejects file, never to the candidates | G4 `PreschoolMatcher` | DESIGN. TEST: exact-match only. **NOT RUN** |
| G24 | Web BFF abused to reach the agent with a forged address | The BFF forwards the visitor address and its shared secret. It checks the `Origin` header on the agent route. The MCP service honours the forwarded address only with the shared secret (constant-time match, as Reviews) | `web/app/src/app/api/agent/**` (G7), `BFF_SHARED_SECRET` | DESIGN. **NOT RUN** |
| G25 | Logs carry message text or review text | Logs carry the route, the outcome, the model name and the token counts. Never the question, the answer, a review or a document | All G services | DESIGN. Log review **NOT RUN** on a deployment |
| G26 | Architecture rules pass without the code | `AiBoundaryTests` loads each assembly, asserts its AI namespace exists, and asserts the scan found the existing Api client. The MCP project joins the test project once it exists | `tests/SchoolSearch.Architecture.Tests/AiBoundaryTests.cs` | DESIGN. **NOT RUN** until CI builds the test project |
| G27 | An identity or billing path appears in the agent | The MCP assembly is in the identity and monetisation source lists. It validates no token, mints none and has no billing reference | `IdentityBoundaryTests.cs`, `MonetisationBoundaryTests.cs` | DESIGN. **NOT RUN** until CI |

## 3. Residual risks, not solved in this change

- **No live model call has been made.** Every prompt, parser and guard has been written against the proven request shape
  and tested with a fake client only. The eval sets that decide whether a feature may be enabled are in
  `docs/ai/AI-EVALS.md` and are not yet built or run.
- **Prompt injection is mitigated, not eliminated.** The mitigations are structural (delimiters, escaping, schema
  validation, no side effects). A model can still be steered into a bad sentence inside an allowed schema. The grounding
  check and the human review of every extractor output are the second controls.
- **The MCP service is anonymous in v1 and now holds a credential.** The agent is the most expensive endpoint in the
  estate. Its limits are per process and reset on restart, and a second instance has its own counts. The exit condition is
  in ADR-0017.
- **Reviews are personal data of a kind.** Review text can contain names of teachers or pupils. Triage (Phase E) labels
  them but does not remove them. The agent receives them as data. Retention and the legal basis for sending review text to
  a third party are open (ADR-0011 O1 and O2, legal review D3). **The AI features that send review text must not be enabled
  in production until those are closed.**
- **Third-party processing.** Requests go to the Anthropic API. The data-processing terms and the retention setting for the
  key are not verified in this repository. Recorded as open in ADR-0011.
- **A single Postgres instance and an in-memory budget** (ADR-0015 and ADR-0011 item 11) are unchanged by this phase.

## 4. What the review did not run

| Check | State |
|---|---|
| Unit and integration tests of the new Api, MCP and Import code | NOT RUN in this change set (no .NET SDK in the authoring environment; CI runs them once the code is merged) |
| Architecture rules (`AiBoundaryTests`, identity and monetisation rules with the MCP assembly) | NOT RUN |
| Live model calls for any feature | NOT RUN. No eval has been run. Enabling any feature in production is blocked until `docs/ai/AI-EVALS.md` is met |
| Agent flow in the browser (`/doradca`) and the degraded states (Playwright) | NOT RUN in CI with a key; the degraded state is the only one CI exercises |
| actionlint on the workflows | PASS for `.github/workflows/*.yml` with actionlint 1.7.7 (run by the author) |
| Fly deploy of the key push | NOT RUN. It runs on a `v*` tag only |
| Extractor run on a real gmina document | NOT RUN |
