# Recorded fixtures for the offline selftest

One folder per feature: `evals/fixtures/<feature>/*.json`. The selftest (`node evals/run.mjs --selftest`) runs each
feature's grader on these files, with no network and no key.

```json
{
  "caseId": "explain-001",
  "shouldPass": true,
  "response": { "status": 200, "body": { "text": "..." } },
  "note": "optional: why this answer is good or bad",
  "synthetic_pii": false
}
```

- `caseId` must exist in `evals/datasets/<feature>/cases.jsonl`.
- `response` has the same shape the grader receives at run time: `{status, body}`. `body` is the parsed JSON.
- `shouldPass: true` is a good answer the grader must pass. `shouldPass: false` is a bad answer (one per hard rule,
  as AI-EVALS section 4 asks) that the grader must fail. A feature needs at least one of each.
- Invented content only. The selftest flags e-mail, phone, PESEL and postal-code shapes in fixtures, as it does in
  datasets, unless `synthetic_pii` is `true` for an obviously fake pattern.
- Each file must be valid UTF-8 JSON. The selftest runs each grader twice and requires the same answer.
