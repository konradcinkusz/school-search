# Visual harness (local, mock backends)

Screenshots and automated findings for every public route of the web app at three viewports, served against a
synthetic catalog. The real catalog is .NET and needs an SDK this machine does not have, so `mock-api.mjs` answers its
routes instead. Nothing here is a real school, pupil, street or exam: every row carries the source
`DEMO SEED (synthetic)`.

## Files

| File | Purpose |
| --- | --- |
| `mock-api.mjs` | Dependency-free Node HTTP mock. Catalog on `MOCK_PORT` (8080), reviews 8082, billing 8083, agent 8084. Serves `/alive` on each. |
| `playwright.visual.config.ts` | Separate Playwright config (chromium from `/opt/pw-browsers`). Projects `phone` 360x740, `tablet` 768x1024, `desktop` 1366x850. Not used by CI (`tests/e2e/playwright.config.ts` only reads `tests/e2e/tests`). |
| `visual.spec.ts` | Visits each route in each project, takes full-page screenshots, records findings. |
| `run.sh` | Starts the mock, builds and starts the web app, runs the spec, stops the background processes. |
| `out/` | Output (gitignored): screenshots, `report.json`, `results/`, `logs/`. |

## Run

```sh
tests/e2e/visual/run.sh               # build, start mock and web app on :3000, collect, stop
SKIP_BUILD=1 tests/e2e/visual/run.sh  # reuse the existing web/app/.next build
```

Only the mock and the web app are started by the script. Playwright is run from `tests/e2e`, which owns
`@playwright/test`. Do not run `playwright install`: the script points at the chromium already under `/opt/pw-browsers`.
Set `VISUAL_CHROMIUM_PATH` to override the binary.

To run the mock on its own:

```sh
node tests/e2e/visual/mock-api.mjs            # MOCK_LOG=1 logs every request
curl -s localhost:8080/api/v1/schools/demo-sp-1
```

## Routes

Public routes from the task, plus a few extra states that exercise the demo seed:

- `/` and `/?gmina=9999999&ulica=Przykladowa&numer=5` (catchment result)
- `/szkoly/demo-sp-1` (published score), `demo-sp-3` (insufficient cohort), `demo-sp-6` (no exam data)
- `/ranking/country`, `/ranking/gmina:9999999`, `/porownaj?slugs=demo-sp-1,demo-sp-7`
- `/przedszkola`, `/przedszkola/przedszkole-demonstracyjne-nr-1`
- `/metodologia`, `/dla-szkol`, `/doradca`, `/cennik`, `/kontakt`, `/regulamin`, `/prywatnosc`, `/zglos-tresc`
- `/konto/logowanie`, `/konto/rejestracja`
- `/dla-szkol/zgloszenie/demoTokenVisual0000000001` (correction status, mock token)
- a 404 page

The school route is `/szkoly/<slug>` in the app (the task text said `/szkola/`).

## Output

- `out/<viewport>/<route-slug>.png`: full-page screenshot, one per route per viewport.
- `out/report.json`: the collector report. Its top level has `generatedAt`, `baseURL`, `note`, `summary` and `pages`.
  `summary` counts pages with load failures, 5xx answers, console errors, page errors, horizontal overflow, small
  targets, images without alt and unnamed form fields, plus the totals behind them.
- `pages[]` has one entry per route and viewport:
  - `status`, `loadError`, `durationMs`, `screenshot`
  - `consoleErrors`, `pageErrors`, `failedRequests`
  - `audit.overflow`: `scrollWidth`, `clientWidth`, `overflows`. Overflow means `scrollWidth > clientWidth`.
  - `audit.smallTargets`: visible links, buttons and fields under 44 x 44 CSS px, with `count` and up to 40 samples
    (`selector`, `text`, `width`, `height`, `inlineLink`). `inlineLink` marks links inside running text. They are
    reported as well, so decide case by case whether they need a larger target.
  - `audit.imagesWithoutAlt`: `<img>` with no `alt` attribute. `alt=""` is treated as decorative and not reported.
  - `audit.unnamedFields`: inputs, selects and textareas with no accessible name. Placeholder-only fields are flagged
    with the reason `placeholder only (not a label)`.

Findings never fail the run. The spec fails only when a page cannot load, returns no HTTP status, or answers 5xx.
A 404 answer on the not-found route is expected and passes.

## Mock coverage

Catalog (`/api/v1/...`): `gminas`, `schools` (q, gmina, isPublic, lat/lon/radiusKm, sort, page, pageSize),
`schools/{slug}`, `schools/by-id/{id}`, `ranking` (scope, gmina, year, page), `compare`, `methodology`, `catchment`,
`corrections/{token}` (GET) and `corrections` (POST: body `{slug, field, message, contactEmail?, kind?}`, checked as
`CorrectionRequestValidator` does, returns 201 with `{token, status}`), `preschools` (filters),
`preschools/{slug}`, `meta`. The AI routes (`schools/{slug}/explain`, `ai/search-intent`, `ai/methodology-answer`)
answer 503 `ai_unavailable`, as the real service does without a key.

Reviews (8082): `schools/{slug}/reviews` (empty list, same envelope as `ReviewsEnvelope`), `notices` (POST returns 201
with `{id, ackToken, receivedAt}`, validated as `DsaValidation.ValidateNotice`; GET `notices/{id}?token=` for the
status), `legal/terms`, `legal/contact` (503 `contact_not_configured`), `reviews/mine` (401).

Billing (8083): `plans` (empty, note "Cennik w przygotowaniu."), `schools/{slug}/premium` (tier none),
`legal/enquiry-consent` (draft text).

Agent (8084): `POST /agent/chat` answers 503 `ai_unavailable`.

Not mocked: the auth service (`AUTH_BASE_URL` is not set, so the account pages show their unavailable state, as they
do without auth), writes to reviews and billing, and the `/api/proxy` browser calls. The visual collector does not
click through them.
