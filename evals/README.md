# Evals (Tier A harness)

Offline and live checks for the AI features that have a public HTTP endpoint: explain, search intent, methodology
answers, and the Doradca agent. Specification: `docs/ai/AI-EVALS.md`. Node 22 built-ins only, no npm dependencies.

## Run it

```sh
node evals/run.mjs --selftest          # offline: datasets, fixtures, graders, buildRequest. CI runs this.
node --test 'evals/test/*.test.mjs'    # unit tests of the harness itself. CI runs this too.
```

The selftest never opens a socket and never reads a key. A failure exits 1 and lists every problem.

Live (calls real endpoints, spends model budget, needs an explicit flag):

```sh
EVAL_API_BASE=https://<catalog-api> EVAL_AGENT_BASE=https://<mcp-service> node evals/run.mjs --live
node evals/run.mjs --live --feature explain,search-intent
```

Without `--live` the runner refuses to make network calls. The GitHub workflow `.github/workflows/evals.yml` does the
same thing on demand (`workflow_dispatch`, never on pull requests) and uploads `evals/out/`.

| Variable | Meaning | Default |
|---|---|---|
| `EVAL_API_BASE` | Catalog API base (`/api/v1/ai/...`). http or https, no query, no credentials | unset: API features NOT RUN |
| `EVAL_AGENT_BASE` | MCP service base (`/agent/chat`) | unset: agent NOT RUN |
| `EVAL_TOKEN_BUDGET` | Cap on **estimated** tokens for the whole run (1 token per 4 characters of request and response JSON) | 200000 |
| `EVAL_MAX_CASES` | Cap on cases per feature. A capped run is marked `partial` | all |
| `EVAL_PACE_MS` | Pause between calls | 1000 |
| `EVAL_TIMEOUT_MS` | Timeout per call. One attempt, no retry | 30000 |
| `EVAL_MODEL_ID`, `EVAL_PROMPT_VERSION` | Recorded in the report only; nothing is sent | not recorded |

## Verdicts and exit codes

Each feature gets a report `evals/out/<feature>-<UTC stamp>.json` and `.md` (the folder is gitignored). The report holds
case ids, classes, statuses, check names and short details. It never holds request or response bodies.

| Verdict | Meaning |
|---|---|
| `PASS` | Every threshold in `thresholds.json` is met on the cases that ran |
| `FAIL` | A threshold is missed. Exit code 1 |
| `NOT RUN` | At least one case could not reach the model: `503 ai_unavailable` or `ai_budget_exhausted` outside the Degradation class, a missing base URL, or an exhausted token budget. Never a pass |
| `NOT GATED` | No entry in `thresholds.json`. Reported, not judged |

`partial: true` means `EVAL_MAX_CASES` cut the set. Exit codes: 0 ok (including NOT RUN; read the verdict), 1 a
threshold failed or the selftest failed, 2 usage or configuration error (bad variable, invalid dataset, unknown feature).

In the Degradation class a 503 is the behaviour under test, so the grader sees it and decides.

## Layout

```
evals/
  run.mjs                 CLI: --selftest | --live [--feature a,b]
  package.json            type module; scripts only
  thresholds.json         per feature; PROPOSALS until "decided_by_owner": true
  lib/
    registry.mjs          grader registry (API below)
    schema.mjs            dataset schema, validator, loader
    pii.mjs               PII shapes used by validators (reports the kind, never the value)
    http.mjs              endpoint call, one attempt, timeout, token budget, base URL check
    runner.mjs            runs one feature's cases and grades them
    thresholds.mjs        thresholds validation, evaluation, verdict
    report.mjs            summaries, sanitising, JSON and Markdown reports
    features.mjs          feature module discovery and validation
    selftest.mjs          the offline checks
    live.mjs              the live run
    args.mjs, errors.mjs, paths.mjs
  features/               one module per feature, found by file name; "_" prefix is skipped
    _example.mjs          stub that shows the contract
  datasets/<feature>/     cases.jsonl + README.md (owned by the feature's author)
  fixtures/<feature>/     recorded responses for the selftest (see fixtures/README.md)
  test/                   node:test unit tests of the harness
  out/                    reports (gitignored)
```

## Registry API (`lib/registry.mjs`)

```js
registerGrader(feature: string, fn: (evalCase, response) => GradeResult | Promise<GradeResult>): fn
getGrader(feature: string): fn | undefined
hasGrader(feature: string): boolean
listGraders(): string[]
normalizeGradeResult(result: unknown): { pass: boolean, checks: { name, pass, detail }[] }   // throws TypeError
grade(feature: string, evalCase, response): Promise<{ pass, checks }>                        // throws if none registered
clearGraders(): void                                                                          // tests only
```

- `feature` is lower-case with hyphens, equal to the dataset folder and the module file name. Registering twice throws.
- `evalCase` is one line of `cases.jsonl`, as parsed.
- `response` is `{ status: number, body: unknown }`. `body` is parsed JSON, or the raw text when it is not JSON.
  Graders must not read anything else (latency is not passed).
- `GradeResult.checks` must be non-empty. `pass` must equal "every check passed"; a mismatch is rejected.
- Graders are deterministic. Prefer structural checks (status, `error.code`, enums, numbers grounded in the facts)
  over phrase matching. The optional model judge is not part of Tier A.

## Feature module contract (`evals/features/<feature>.mjs`)

```js
import { registerGrader } from '../lib/registry.mjs';
export const feature = 'explain';                 // must equal the file name
export const target = 'api';                      // 'api' -> EVAL_API_BASE, 'agent' -> EVAL_AGENT_BASE
export function buildRequest(evalCase) {          // pure; no network
  return { method: 'POST', path: '/api/v1/ai/...', body: { ... } };   // method GET|POST (default POST); path starts with /
}
registerGrader(feature, grade);                   // top level, once
```

The selftest imports every module, checks that the grader is registered, runs `buildRequest` over every case, and grades
each fixture twice.

## Dataset format (`evals/datasets/<feature>/cases.jsonl`)

One JSON object per line, UTF-8, no BOM, no blank lines:

```json
{"id":"explain-001","class":"Happy path","input":{...},"expect":{...},"notes":"why this case exists","synthetic_pii":false}
```

- `id`: `<feature>-<nnn>`, unique across all datasets, stable once published.
- `class`: exactly one of `Happy path`, `Ambiguity`, `Denied path`, `Adversarial`, `Degradation` (AI-EVALS section 2).
- `input`, `expect`: objects. `expect` describes what a correct output satisfies, not a golden string.
- `notes`: string, required. `synthetic_pii`: optional boolean, only for obviously fake PII shapes.
- No key other than these six. Unknown keys are errors.
- At least 40 cases per dataset. At least one each of Happy path, Adversarial and Degradation.
- The validator rejects e-mail, phone, PESEL and postal-code shapes anywhere in a case unless `synthetic_pii` is true.
  Error messages give the path and the kind, never the value.
- Each dataset folder has a `README.md` (what is covered, how the cases were derived, how to extend).

## Thresholds (`evals/thresholds.json`)

```json
"explain": { "decided_by_owner": false, "source": "docs/ai/AI-EVALS.md section 3, Feature 1", "overall": 1.0,
             "perClass": { "Happy path": 0.95 } }
```

`overall` and `perClass` are pass rates in 0..1 over evaluated cases. A class with no evaluated case fails a full run and
is skipped in a partial one. Every entry is a proposal until the owner sets `decided_by_owner: true`, with the date, in
`docs/ai/AI-EVALS.md`.

## Adding or changing a feature

1. `evals/datasets/<feature>/cases.jsonl` and `README.md`; then `evals/fixtures/<feature>/*.json` with at least one
   `shouldPass: true` and one `shouldPass: false` per hard rule.
2. `evals/features/<feature>.mjs`: export `feature`, `target`, `buildRequest`; register the grader.
3. `node evals/run.mjs --selftest` and `node --test 'evals/test/*.test.mjs'` must pass.
4. Add an entry to `thresholds.json` with `decided_by_owner: false`.
5. A live run is a person's task (`docs/runbooks/AI-OPERATIONS.md` section 5), not a CI step.

Harness changes need unit tests in `evals/test/`.
