// Report building and writing. Reports never contain request or response bodies, only case ids, classes,
// check names and short details. Every string passes through sanitize() first.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { SCENARIO_CLASSES } from './schema.mjs';
import { decideVerdict, evaluateThresholds } from './thresholds.mjs';

export const DETAIL_LIMIT = 300;

const SECRET_NAME = /SECRET|PASSWORD|PEPPER|API_KEY|_TOKEN$/i;

// Values of secret-looking environment variables, so that a report can never print them.
export function secretValues(env = process.env) {
  return Object.entries(env)
    .filter(([name, value]) => SECRET_NAME.test(name) && typeof value === 'string' && value.length >= 8)
    .map(([, value]) => value);
}

export function sanitize(text, { limit = DETAIL_LIMIT, secrets = secretValues() } = {}) {
  let out = String(text ?? '');
  out = out.replace(/sk-ant-[A-Za-z0-9_-]+/g, '[redacted]');
  for (const secret of secrets) out = out.split(secret).join('[redacted]');
  // Control characters (except tab and newline) can break the Markdown table and terminal output.
  out = out.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, '');
  if (out.length > limit) out = `${out.slice(0, limit)}...`;
  return out;
}

// Pass rates per class and overall, from the case results. rate is null when nothing was evaluated.
export function summarize(caseResults) {
  const perClass = {};
  for (const scenarioClass of SCENARIO_CLASSES) {
    perClass[scenarioClass] = { total: 0, evaluated: 0, passed: 0, failed: 0, rate: null };
  }
  let total = 0;
  let evaluated = 0;
  let passed = 0;
  let failed = 0;
  let notRun = 0;
  for (const result of caseResults) {
    total += 1;
    const stats = perClass[result.class] ?? (perClass[result.class] = { total: 0, evaluated: 0, passed: 0, failed: 0, rate: null });
    stats.total += 1;
    if (result.status === 'not_run') {
      notRun += 1;
      continue;
    }
    evaluated += 1;
    stats.evaluated += 1;
    if (result.status === 'pass') {
      passed += 1;
      stats.passed += 1;
    } else {
      failed += 1;
      stats.failed += 1;
    }
  }
  for (const stats of Object.values(perClass)) {
    stats.rate = stats.evaluated > 0 ? stats.passed / stats.evaluated : null;
  }
  return {
    total,
    evaluated,
    passed,
    failed,
    notRun,
    overallRate: evaluated > 0 ? passed / evaluated : null,
    perClass,
  };
}

export function buildReport({
  feature,
  target,
  baseHost,
  startedAt,
  finishedAt,
  modelId,
  promptVersion,
  budget,
  datasetTotal,
  caseResults,
  thresholdEntry,
  secrets,
}) {
  const clean = (text, limit) => sanitize(text, { limit, secrets });
  const summary = summarize(caseResults);
  const notRunReasons = [...new Set(caseResults.filter((r) => r.status === 'not_run').map((r) => clean(r.reason, 120)))];
  const notRun = notRunReasons.length > 0;
  const partial = caseResults.length < datasetTotal;
  const thresholds = thresholdEntry
    ? evaluateThresholds(summary, thresholdEntry, { partial })
    : { failed: false, checks: [] };
  const verdict = decideVerdict({ notRun, hasEntry: Boolean(thresholdEntry), thresholdFailed: thresholds.failed });
  return {
    schemaVersion: 1,
    tier: 'A',
    feature,
    target,
    baseHost,
    mode: 'live',
    startedAt,
    finishedAt,
    modelId: modelId ? clean(modelId, 80) : null,
    promptVersion: promptVersion ? clean(promptVersion, 80) : null,
    verdict,
    partial,
    datasetTotal,
    notRunReasons,
    tokens: { estimated: true, limit: budget.limit, spent: budget.spent },
    summary,
    thresholds: thresholdEntry
      ? {
          decided_by_owner: thresholdEntry.decided_by_owner,
          source: clean(thresholdEntry.source, 200),
          checks: thresholds.checks.map((check) => ({
            name: clean(check.name, 80),
            threshold: check.threshold,
            actual: check.actual,
            pass: check.pass,
            ...(check.detail ? { detail: check.detail } : {}),
          })),
        }
      : null,
    cases: caseResults.map((result) => ({
      id: result.id,
      class: result.class,
      status: result.status,
      ...(result.reason ? { reason: clean(result.reason, 120) } : {}),
      latencyMs: result.latencyMs,
      tokensEstimated: result.tokensEstimated,
      checks: result.checks.map((check) => ({
        name: clean(check.name, 80),
        pass: check.pass,
        detail: clean(check.detail, DETAIL_LIMIT),
      })),
    })),
  };
}

const pct = (rate) => (rate === null ? 'n/a' : `${(rate * 100).toFixed(1)}%`);

export function renderMarkdown(report) {
  const lines = [];
  lines.push(`# ${report.feature}: ${report.verdict}`);
  lines.push('');
  lines.push('Tier A live run. Graders are rule-based; no model judge contributed to these numbers.');
  lines.push('');
  lines.push(`- Target: ${report.target} (host ${report.baseHost ?? 'not set'})`);
  lines.push(`- Started: ${report.startedAt}; finished: ${report.finishedAt}`);
  lines.push(`- Model id: ${report.modelId ?? 'not recorded (set EVAL_MODEL_ID)'}`);
  lines.push(`- Prompt version: ${report.promptVersion ?? 'not recorded (set EVAL_PROMPT_VERSION)'}`);
  lines.push(`- Cases: ${report.summary.total} run of ${report.datasetTotal}${report.partial ? ' (partial, EVAL_MAX_CASES)' : ''}; `
    + `passed ${report.summary.passed}, failed ${report.summary.failed}, not run ${report.summary.notRun}`);
  lines.push(`- Estimated tokens: ${report.tokens.spent} of ${report.tokens.limit ?? 'no'} budget (estimate, 1 token per 4 characters)`);
  if (report.notRunReasons.length > 0) {
    lines.push(`- NOT RUN reasons: ${report.notRunReasons.join('; ')}`);
  }
  lines.push('');
  lines.push('## Pass rates by class');
  lines.push('');
  lines.push('| Class | Cases | Evaluated | Passed | Rate |');
  lines.push('|---|---:|---:|---:|---:|');
  for (const [scenarioClass, stats] of Object.entries(report.summary.perClass)) {
    if (stats.total === 0) continue;
    lines.push(`| ${scenarioClass} | ${stats.total} | ${stats.evaluated} | ${stats.passed} | ${pct(stats.rate)} |`);
  }
  lines.push(`| **All** | ${report.summary.total} | ${report.summary.evaluated} | ${report.summary.passed} | ${pct(report.summary.overallRate)} |`);
  lines.push('');
  lines.push('## Thresholds');
  lines.push('');
  if (!report.thresholds) {
    lines.push('No entry in evals/thresholds.json: this feature is not gated.');
  } else {
    const status = report.thresholds.decided_by_owner ? 'set by the owner' : 'PROPOSAL, not decided by the owner';
    lines.push(`Source: ${report.thresholds.source}. Status: ${status}.`);
    lines.push('');
    lines.push('| Check | Threshold | Actual | Result |');
    lines.push('|---|---:|---:|---|');
    for (const check of report.thresholds.checks) {
      const result = check.pass === null ? 'skipped' : check.pass ? 'ok' : 'BELOW';
      lines.push(`| ${check.name} | ${pct(check.threshold)} | ${pct(check.actual)} | ${result} |`);
    }
  }
  lines.push('');
  const problems = report.cases.filter((c) => c.status !== 'pass');
  lines.push('## Cases that did not pass');
  lines.push('');
  if (problems.length === 0) {
    lines.push('None.');
  } else {
    for (const problem of problems) {
      lines.push(`- ${problem.id} (${problem.class}): ${problem.status}${problem.reason ? ` (${problem.reason})` : ''}`);
      for (const check of problem.checks.filter((c) => !c.pass)) {
        lines.push(`  - ${check.name}: ${check.detail}`);
      }
    }
  }
  lines.push('');
  return lines.join('\n');
}

// Writes <feature>-<stamp>.json and .md into outDir (created if missing). Returns the two paths.
export function writeReport({ outDir, feature, stamp, report }) {
  mkdirSync(outDir, { recursive: true });
  const base = `${feature}-${stamp}`;
  const jsonPath = join(outDir, `${base}.json`);
  const mdPath = join(outDir, `${base}.md`);
  writeFileSync(jsonPath, `${JSON.stringify(report, null, 2)}\n`, 'utf8');
  writeFileSync(mdPath, renderMarkdown(report), 'utf8');
  return { jsonPath, mdPath };
}

// 2026-10-11T16:00:00.000Z -> 20261011T160000Z (safe in file names)
export function stampFor(date) {
  return date.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z');
}
