# Dataset: search-intent (AI-FEATURES feature 2, "Doprecyzuj wyszukiwanie")

Endpoint: `POST /api/v1/ai/search-intent`, body `{question}` (src/SchoolSearch.Api/Ai/SearchIntent*.cs).
Grader: `evals/features/search-intent.mjs`. Fixtures: `evals/fixtures/search-intent/`.

## What is covered (127 cases, `cases.jsonl`)

| Class | Count | What |
|---|---|---|
| Happy path | 60 | Templated combinations of: a seven-digit TERYT code written in the question (9999999 or 9999998), school or preschool, "tylko publiczne", a distance (3, 5, 12 km, or 80 km clamped to 50), and one keyword phrase. |
| Ambiguity | 20 | No place named, a city or district name without a code, partial or wrong-length codes (4, 6, 8 digits), pronouns, vague intents, two codes in one question. Expectation: nothing guessed. |
| Denied path | 20 | Quality and ranking requests in Polish and English ("najlepsza", "ranking", "najgorsza", "polecane", "best", "top 3"), sorting requests ("sortuj", "malejąco", "ascending"). Expectation: `unsupported` names the request and `q` carries no ranking word. |
| Adversarial | 22 | Injected instructions (type, distance, public flag, role change, fake system or admin lines, closing-tag injection, format change, prompt or key disclosure, a repeated gmina code), markup in the question, an empty question, a 301-character question, and five PII-shaped questions (e-mail, phone, PESEL, postal code), marked `synthetic_pii: true`. |
| Degradation | 5 | 503 `ai_unavailable` (twice), 503 `ai_budget_exhausted`, 429, 502 `ai_output_rejected`. All marked `expect.offlineOnly: true`. |

## How the cases were derived

- The happy-path questions are generated from a template in the scratch generator used to write this file (combinations of
  the phrases above), so every expected filter follows from its phrase. Hand-written cases cover the other classes.
- The expected filter values follow the parser rules in `SearchIntentParser.cs` and the enum and range rules in the
  prompt: TERYT is seven digits and appears in the question, distance is capped at 50, `type` is `school` or `preschool`.
- The quality and sort vocabulary is the product's list (`SearchIntentGuard.cs`) plus the adverb forms `najlepiej`,
  `najgorzej`, `najwyżej`, `najniżej`, which the grader requires to stay out of `q` as well.

## What the grader checks

Exact contract keys (top level and filters); `aiLabel`; seven-digit gmina that occurs in the question; `type` in its enum;
`publicOnly` true or null; distance in (0, 50]; `q` at most 80 characters, no angle brackets, no ranking, sort or
personal-data shape; `unsupported` names the quality or sort request when the case says so, and is empty otherwise; no
prompt, rule, key or result text in the answer; expected filter values; `nullFields` for ambiguity cases.

## Known gaps (found while writing the cases; the cases stay as they are)

- `najlepiej` (and `najgorzej`) are not in `SearchIntentGuard.RankingWords`, so a `q` containing them passes the product's
  checks. The agent prompt (`AgentPrompts.cs`) already lists `najlepiej`. Case `search-intent-099` and its fixture
  `bad-najlepiej-in-q` show the gap.
- The gmina code is checked for shape and for presence in the question, not for existence. `0000000` is accepted if typed
  (case `search-intent-111`).
- The answer is parsed from JSON text in the model reply, not from a strict tool call as AI-FEATURES section 4 proposes.
  The grader checks the contract as implemented.

## How to extend

Add a line with the next free id (`search-intent-128`, ...). Use invented places (the demo gmina 9999999 or 9999998 only)
and invented names. Personal-data-shaped strings need `synthetic_pii: true`. Run `node evals/run.mjs --selftest`.
