# AI operations runbook

How the AI features are configured, switched on and off, paid for, evaluated and kept honest. Decisions:
[ADR-0011](../adr/0011-anthropic-api-usage-boundaries.md) (boundaries), [ADR-0016](../adr/0016-billing-service-and-provider-neutral-webhook.md)
(billing assistant), [ADR-0017](../adr/0017-ai-agent-in-mcp-service-and-batch-extractors.md) (where the agent and the extractors
run). Feature by feature: [`docs/ai/AI-FEATURES.md`](../ai/AI-FEATURES.md). Measures before enabling: [`docs/ai/AI-EVALS.md`](../ai/AI-EVALS.md).
Security: [`docs/security/SECURITY-REVIEW-PHASE-G.md`](../security/SECURITY-REVIEW-PHASE-G.md).

Status at writing: **no feature is enabled in any deployed environment.** Every `AI_ENABLED` switch is `false`, and no live
model call has been made by this repository.

## 1. Which service holds what

| Feature | Service | Key | Kill switch in `[env]` | Budget variable |
|---|---|---|---|---|
| 1 Explain the result | `school-search-api-dev` | `ANTHROPIC_API_KEY` (Fly secret) | `AI_ENABLED` in `flyio/api.fly.toml` | `AI_DAILY_TOKEN_BUDGET` (200000) |
| 2 Search intent | `school-search-api-dev` | same | same | same (shared counter with feature 1) |
| 5 Correction triage (moderator) | `school-search-api-dev` | same | same | same |
| 7 Methodology answers | `school-search-api-dev` | same | same | same |
| 4 Review triage | `school-search-reviews-dev` | same | `AI_ENABLED` in `flyio/reviews.fly.toml` | `AI_DAILY_TOKEN_BUDGET` (100000) |
| 8 Profile-copy assistant | `school-search-billing-dev` | same | `AI_ENABLED` in `flyio/billing.fly.toml` | `AI_DAILY_TOKEN_BUDGET` (100000) |
| Doradca (agent) | `school-search-mcp-dev` | same | `AI_ENABLED` in `flyio/mcp.fly.toml` | `AI_DAILY_TOKEN_BUDGET` (100000) |
| 3 Catchment extractor | operator's machine | `ANTHROPIC_API_KEY` in the operator's environment | none: it runs only when a person runs it | the CLI's own budget (section 4) |
| 6 Preschool tuition extractor | operator's machine | same | none | same |

The extractors are never deployed and never see the Fly secrets (ADR-0017 decision 3).

## 2. Keys

- **Creating the key.** Create it in the Anthropic console for this project only. Never reuse a personal key. Record the
  owner and the date in the operator's own notes, not in this repository.
- **Storing the key for CI deploys.** GitHub environment `dev`, secret `ANTHROPIC_API_KEY`. `flyio.yml` pushes it to the four
  apps above with `flyctl secrets import --stage` on stdin, and only when the secret is set. An empty secret is skipped
  silently.
- **Storing the key locally.** `scripts/setup.sh` asks for it and stores it in `dotnet user-secrets` (Api). The value is
  never written to `secrets.env.example`, `.env` or a committed file. Gitleaks runs in the hook and in CI.
- **Rotating the key.** Create the new key, set the GitHub secret, redeploy the four apps from the same tag (their
  `--stage` import takes effect on deploy), then revoke the old key in the console. A rotation changes no stored data.
- **Checking a key is set without reading it.** `flyctl secrets list --app <app>` shows names and digests only. It never
  shows a value.

## 3. Switching a feature on or off

The kill switch is the `AI_ENABLED` line in the `[env]` section of the service's `flyio/*.fly.toml`. It is committed, reviewed and
shipped with a tag.

- **To switch a feature on:** the feature's eval gate in `docs/ai/AI-EVALS.md` is met and recorded; the open items named for that
  feature are closed (ADR-0011 O1 retention and O2 payload allowlist; for triage, legal review D3); a reviewer approves the
  one-line change; the tag is pushed. The API answers 200 and `/health` lists `anthropic` as `ok` after the first call.
- **To switch a feature off in an incident:** the quickest reviewed route is a commit that sets `AI_ENABLED = "false"` (or
  leaves it false) and a tag. The faster operational route is to remove the key from that one app:
  `flyctl secrets unset ANTHROPIC_API_KEY --app <app>`. This restarts the app, and the feature then answers 503
  `ai_unavailable`, with the pages in their degraded state. Record the action in the incident note and restore the key
  only after the cause is understood.
- **Expected degraded state.** Without the key or with the switch off: 503 `ai_unavailable`; the moderator queue has no labels;
  the agent page says that the advisor is unavailable; the search box says that language search is unavailable and the
  filter form still works. Nothing else changes (P8).

## 4. Running the extractors (features 3 and 6)

Run only by a person, on a trusted machine, against a document that a person saved. Never in CI and never on Fly.

1. **Save the source.** Save the gmina resolution or the school tuition page to a local file, text or HTML or CSV. Note the
   URL and the date you read it. The command reads the file and never fetches the URL.
2. **Set the key in your environment only:** `export ANTHROPIC_API_KEY=...` in the shell you run the command from. Do not
   put it in a file in the repository, and do not pass it on the command line.
3. **Run the command** (the flags are the ones in `docs/ai/AI-FEATURES.md`, sections 3 and 6):
   - `catchment-extract --input <file> --gmina <TERYT> --resolution-url <url> --resolution-date <yyyy-MM-dd> --out <csv> [--map-schools <file>]`
   - `preschool-extract --input <file> --gmina <TERYT> --source-name <name> --source-url <https url> --as-of <date> --out <csv> --preschools <csv>`
4. **Read the review file before anything else.** Each candidate row is in `<out>.review.md` with the source quote beside it.
   Check every row against the saved source: the name, the street, the house-number rule, the amount and the date.
5. **Check the rejects.** Rows that did not match a school name exactly, or that failed the strict schema, are in the rejects file.
   Fix them by hand or leave them out. Nothing is matched by a guess.
6. **Flip the confidence by hand.** Catchment rows are `UNVERIFIED` until a person removes that header line after sign-off.
   Preschool rows are `unverified` until a person changes the column to `reported`. There is no flag that does it for you.
7. **Load through the existing path only.** `catchment` or `preschool-facts` import command, with the usual dry run first (see
   `docs/runbooks/IMPORT.md`). Keep the review file with the candidate file as the record of who checked what.

**Budgets for the CLI.** The extractors have a token budget of their own, set in the Import code (`ImportTokenBudget`). A run
that reaches it stops and says so. It writes no partial file that looks complete.

## 5. Evals and the gate

- Each feature has an eval set and a pass threshold in `docs/ai/AI-EVALS.md`. A feature is not switched on until its set is
  built and its threshold is met on the pinned model (`AI_MODEL`, default `claude-haiku-5-5`).
- A change of model, prompt version or effort setting is a new eval run. The result is recorded with the date and the model id.
- Eval sets contain no real pupil data and no real review text. Use synthetic or public examples.
- The eval run is a person's task. It is not automated in CI, because it calls the live model and spends the budget.

## 6. Monitoring and cost

- **Health.** `/health` on each service lists `anthropic` as `disabled` (no key or switch off), `ok` (the last call succeeded) or
  `degraded` (repeated 429 or 5xx). A degraded integration is a signal to check the budget and the status page, not to retry in a loop.
- **Budget exhaustion.** Answers 503 `ai_budget_exhausted` (or the feature's fixed message). The counter resets at UTC midnight
  and on a restart. A restart therefore gives a second allowance: the cap is soft (ADR-0011 item 11). Look at the Anthropic
  console for the real spend.
- **Per-IP and per-subject limits.** Each feature has its own; they are in memory, per process, and reset on a restart.
- **Metrics.** Labelled by feature and model only. Never by school, query, review or subject.
- **Alert threshold (proposed).** Spend above 80% of the day's budget on any service. The alert is manual until a metric exists.

## 7. What to do when something looks wrong

| Symptom | First action | Then |
|---|---|---|
| A reply contains a number not in the facts | Switch the feature off (section 3) | Read the reply and the facts in the logs by type, not by text; add the case to the eval set |
| A reply recommends or ranks a school | Switch the feature off | The prompt rule failed. Fix the prompt and the guard, add the case to the eval set, re-run the eval before switching on |
| A review or a document looked like an instruction and changed output | Switch the feature off | Record it in the security review and add the case to the injection set |
| The key appears in a log, a commit or a message | Revoke the key in the console immediately | Rotate (section 2), check the log retention, and record an incident in `docs/security/` |
| Unexpected spend | Switch off all `AI_ENABLED` (section 3) | Check the console, the rate limit values and the budget variables |

## 8. Known gaps

- The budgets and the per-IP allowances are in memory. A second instance of any service gets its own counts.
- Retention of prompts and answers at the provider is not verified in this repository (ADR-0011 O1).
- The payload allowlist for feature 1 is an open item (ADR-0011 O2).
- No live model call has been made in the authoring environment. The first live run of each feature is part of its eval gate.
