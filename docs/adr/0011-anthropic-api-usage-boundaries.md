# ADR-0011: Anthropic API usage boundaries

- **Status:** Accepted for feature 1 only ("Wyjaśnij ten wynik"). Features 2 to 7 are **Proposed** and stay
  Proposed until each has its own eval spec and, where the table in `docs/ai/AI-FEATURES.md` says so, its own
  decision. The retention item under "Open items" is **not confirmed** and blocks any public launch of feature 1.
- **Date:** 2026-10-11
- **Deciders:** owner (Konrad Cinkusz). The status above is the drafter's record for PR 4; the owner confirms it at
  review by merging. The data-retention settings must also be confirmed by the owner (open item O1).

## Context

The product may use Claude through the Anthropic API. The full proposal, with seven features, the data each one
would send and the failure modes, is in `docs/ai/AI-FEATURES.md`. This ADR records the boundaries that apply to
every feature, and the decisions for the one feature that is implemented.

The constraints that bind it are:

- The product scores schools with a counter-metric and a confidence (MASTER-PROMPT §4). `docs/architecture/00-ARCHITECTURE.md`
  requires that every score carries cohort size, interval and methodology version, and that suppression is never
  shown as zero.
- Metric ethics (architecture-standards `docs/guides/METRIC-ETHICS.md`): no number leaves without its confidence (§3),
  heuristics about people stay out of any composite (§4), and the unit of evaluation is the artifact, never a person (§5).
- Pupil names, private-individual operator names, nationality data and any personal identifier must never be sent
  or stored (MASTER-PROMPT §5 and §3).
- Optional dependencies must degrade (P8). Without the key the product works and says so in `/health`.
- Metrics must not carry identifying labels (MASTER-PROMPT §7).

## Decision

**Boundaries that apply to every AI feature (B1 to B10):**

- **B1. AI never computes or alters a score.** No AI output is a score, confidence, cohort size, interval or
  suppression state. The number on screen comes from the Api's score object, not from model text. `Api/Scoring/`
  must not reference any AI type (to be an architecture test; see "Status of enforcement").
- **B2. AI never ranks.** No feature orders schools. Result order stays the fixed API order.
- **B3. Pupil names are never sent.** Olympiad pupil names are transient in memory and never reach a model.
- **B4. Private-individual operator names are never sent.** Operator type only (`idTypOrgProw` 141, 146, 150 stay type-only).
- **B5. No nationality or cudzoziemcy data, ever.**
- **B6. Every AI text is labelled `Wygenerowano przez AI`** (feature 1 uses the label "Wygenerowano przez AI — to nie jest ocena szkoły"), and the structured facts it describes are shown in the same panel.
- **B7. Untrusted text is data, never instructions.** Untrusted text goes only in the user turn. The system prompt is
  operator-written and fixed. No side-effect tools exist. Output is plain text, never HTML.
- **B8. No AI output is published without a human decision,** except text the product renders from the same
  structured facts (the non-AI fallback, which is labelled as automatic description, not AI).
- **B9. Degrade, do not fail (P8).** Without `ANTHROPIC_API_KEY`, or with `AI_ENABLED=false`, the endpoint returns
  503 `ai_unavailable` and the rest of the product is unchanged.
- **B10. No secrets in source; no competitor data.** Keys come from the environment only. No web fetching is part of the AI code.

**Decisions specific to this ADR:**

1. **The Anthropic API is the only LLM provider.** No other provider without a new ADR.
2. **Placement (open decision D1 in `docs/ai/AI-FEATURES.md`).** The client lives in-process in `src/SchoolSearch.Api/Ai/`,
   not in a separate service. Reason: one endpoint, one feature, and no batch job needs the key yet. This is a
   deliberate deviation from P10 (a credential-bearing dependency with an availability risk becomes a service). It is
   recorded in `docs/architecture/00-ARCHITECTURE.md`. Revisit when a batch job (features 3 or 6) needs the key outside the API. The Import
   one-shot machine must not hold the key by default.
3. **Feature implemented: 1 only, "Wyjaśnij ten wynik".** `POST /api/v1/schools/{slug}/explain`. Nothing else.
4. **Model: `claude-haiku-5-5`**, pinned by ID in configuration (`AI_MODEL`), not a `latest` alias. Chosen because
   it is the high-volume tier in the proposal and the task is describing a short fact sheet. A model change is a
   prompt-version change and goes through the eval gate (open item O4).
5. **Call settings.** `MaxTokens` 1500. Adaptive thinking is on by default for this model and counts against that
   limit, so 1500 is a ceiling for thinking plus text, not for text alone. Effort `low` if the SDK exposes it,
   otherwise the default. Temperature is not set.
6. **Data sent.** Per the spec for PR 4: the prompt is built only from the `SchoolProfile` the Api computes for that
   slug (the same read model and score service the web uses), plus the fixed system prompt in Polish.
   **Not verified:** the exact field allowlist in the implemented payload. The proposal in `docs/ai/AI-FEATURES.md`
   (feature 1) excludes school name, operator name, address, gmina name and identifiers from the payload. A
   `SchoolProfile` contains some of these. The payload builder must therefore apply an explicit allowlist, and that
   allowlist must be checked against this ADR before merge (open item O2).
7. **Data never sent.** Pupil names; private-individual operator names; e-mail addresses; IP addresses; account or session
   identifiers; query strings; correction or review text; billing, promotion or entitlement status; nationality data.
   User free text is not part of feature 1 at all.
8. **Output guard.** After the call, every digit sequence in the returned text must appear in the facts payload
   (score, interval bounds, cohort size, exam year, coverage, methodology version). If any does not, the text is
   discarded and the endpoint returns 502 `ai_output_rejected`. Ungrounded numbers are never published.
   The text must also not contain a score when the status is `limited`, `insufficient_cohort`, `no_exam_data` or `no_baseline`.
9. **Refusal and truncation.** A stop reason of `refusal` or `max_tokens` is a failure (502), never an empty success.
10. **Cache.** In-memory, 24 hours, keyed by (slug, methodologyVersion, examYear, lang), size 1 per entry, because the
    shared cache has a size limit. A hit does not consume budget. The cache is per process and is lost on restart.
11. **Budget.** `AI_DAILY_TOKEN_BUDGET`, default 200000 tokens per day, a process-wide counter reset at UTC midnight.
    When spent, the endpoint returns 503 `ai_budget_exhausted`. Input and output token counts are logged. The prompt,
    the output and the key are never logged.
    **Deviation:** the proposal put the counter in Postgres (`ai_usage`). The implemented counter is in memory, so it
    resets on restart and is not shared between instances. This is acceptable for one min-1 machine and is recorded
    in `docs/architecture/00-ARCHITECTURE.md`. Exit: move the counter to Postgres before a second instance runs.
12. **Per-client limit.** 10 explanations per hour per client IP, through the existing client-IP resolver. Cache hits do
    not count against it. The limit keys are never school identifiers or query strings.
13. **Health.** `/health` lists integration `anthropic` as `ok` (key set, last call succeeded), `degraded` (repeated
    429 or 5xx) or `disabled` (key absent or `AI_ENABLED=false`), with a detail string. The startup banner shows the same list.
14. **Metrics** are labelled by feature and model only, never by slug, IP or text.
15. **Evals.** Constraint checks (number grounding, label present, no score when suppressed, no HTML) run in the Api
    test project with a fake `IExplainClient`. No network in tests. A model-calling eval set is not written yet (open item O4).

## Options considered

| Option | For | Against |
|---|---|---|
| Anthropic API, one provider, in-process (chosen) | Smallest surface; one feature; no second deployable | Breaks P10 as written; the key sits in the Api |
| Separate AI service | Isolates the credential and the availability risk | A second service without a current need (MASTER-PROMPT §10 ground rule) |
| Other LLM provider | Possible price or policy advantage | Not evaluated; needs its own ADR |
| Self-hosted model | No data leaves the platform | Not evaluated here |
| AI for scoring or ranking | None that is acceptable | Rejected by B1 and B2 and METRIC-ETHICS §4 |
| Fable 5.1 for all reasoning | Strongest model | Requires 30-day retention; not available under zero data retention without Anthropic authorisation; cost (per `docs/ai/AI-FEATURES.md` §2). Not used |

## Consequences

- **Easier:** a small, auditable surface. Every AI sentence is checkable against the facts beside it.
- **Harder:** every future feature needs its own fallback in the UI, its own payload allowlist and its own eval spec.
  Each feature repeats the same checks.
- **Operational:** the Api now needs `ANTHROPIC_API_KEY` in its secrets when AI is enabled (`flyio/SECRETS.md`).
- **Re-decide later:** D1 placement (when a batch job needs the key), the in-memory budget (before a second instance), and
  any feature beyond 1 (each needs its own record in `docs/ai/AI-FEATURES.md`).

## Status of enforcement

| Boundary | Enforced by | State at PR 4 |
|---|---|---|
| B1 | Architecture test that `Api/Scoring/` has no AI reference | **Not yet written.** Planned, not claimed |
| B3, B4, B5 | Payload allowlist and forbidden-field test | **Partly.** Payload builder is the SchoolProfile path; forbidden-field fixture test to be confirmed (O2) |
| B6 | Response contract carries the label and the facts | Implemented in the web panel and the response shape (to verify on merge) |
| B7 | User turn only; plain-text render | Implemented as a design rule; the web renders text as text |
| B9 | 503 `ai_unavailable`; `/health` entry | Specified; verify on merge |
| Output guard | Digit grounding check, 502 on failure | Specified; a unit test with the fake client is required (verify on merge) |

Nothing in this table has been run in CI by the agent that wrote it. The build was not run in the authoring
environment (no .NET SDK there).

## Open items

- **O1. Data retention and DPA (D2 in `docs/ai/AI-FEATURES.md`).** **Not verified.** The owner must confirm the
  organisation's data-retention settings and zero-data-retention status for the Anthropic API, and record them here.
  Until then feature 1 may run in staging only, and must not be advertised as a public feature.
- **O2. Payload allowlist.** Confirm the exact fields sent for feature 1 against section "Data sent" above, including
  whether `schoolName` and `gminaName` are excluded.
- **O3. Price and limits.** Prices quoted in `docs/ai/AI-FEATURES.md` come from the proposal and were not re-checked
  for this record. Re-check before the budget is set for production.
- **O4. Eval set for feature 1.** Required before the first public deploy: grounding, plain-Polish reading,
  "early, not wrong" framing when confidence is low, coverage mentioned when low, no comparison with other schools.
- **O5. Judge calibration owner (D8).** Not decided.
- **O6. Legal basis for third-party personal data (D3)** is needed before any review feature (feature 4, Phase E).
