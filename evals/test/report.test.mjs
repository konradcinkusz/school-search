import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { TokenBudget } from '../lib/http.mjs';
import { buildReport, renderMarkdown, sanitize, secretValues, stampFor, summarize, writeReport, DETAIL_LIMIT } from '../lib/report.mjs';

const result = (id, scenarioClass, status, extra = {}) => ({
  id, class: scenarioClass, status, checks: status === 'not_run' ? [] : [{ name: 'c', pass: status === 'pass', detail: 'd' }],
  latencyMs: 1, tokensEstimated: 3, ...extra,
});

describe('sanitize', () => {
  test('redacts Anthropic-style keys and known secret values, strips control characters, truncates', () => {
    assert.equal(sanitize('key sk-ant-abc_123-XYZ here'), 'key [redacted] here');
    assert.equal(sanitize('value hunter2hunter2 here', { secrets: ['hunter2hunter2'] }), 'value [redacted] here');
    assert.equal(sanitize('a\u0001b\u007fc'), 'abc');
    assert.equal(sanitize('x'.repeat(DETAIL_LIMIT + 10)).length, DETAIL_LIMIT + 3);
  });

  test('secretValues picks secret-looking variables of useful length only', () => {
    const env = { ANTHROPIC_API_KEY: 'sk-ant-12345678', EVAL_TOKEN_BUDGET: '200000', SHORT_SECRET: 'abc', DB_PASSWORD: 'longpassword' };
    assert.deepEqual(secretValues(env).sort(), ['longpassword', 'sk-ant-12345678']);
  });
});

describe('summarize', () => {
  test('counts per class and overall, leaving NOT RUN out of the rates', () => {
    const summary = summarize([
      result('d-1', 'Happy path', 'pass'),
      result('d-2', 'Happy path', 'fail'),
      result('d-3', 'Degradation', 'not_run'),
      result('d-4', 'Adversarial', 'pass'),
    ]);
    assert.equal(summary.total, 4);
    assert.equal(summary.evaluated, 3);
    assert.equal(summary.notRun, 1);
    assert.equal(summary.overallRate, 2 / 3);
    assert.equal(summary.perClass['Happy path'].rate, 0.5);
    assert.equal(summary.perClass.Degradation.rate, null);
    assert.equal(summary.perClass.Ambiguity.total, 0);
  });
});

describe('buildReport', () => {
  const base = {
    feature: 'demo', target: 'api', baseHost: 'api.test', startedAt: '2026-10-11T10:00:00.000Z',
    finishedAt: '2026-10-11T10:01:00.000Z', modelId: 'claude-haiku-5-5', promptVersion: 'v1',
    budget: new TokenBudget(1000), datasetTotal: 2,
    thresholdEntry: { decided_by_owner: false, source: 'proposal', overall: 1 },
  };

  test('PASS when every evaluated case passes the threshold', () => {
    const report = buildReport({ ...base, caseResults: [result('demo-1', 'Happy path', 'pass'), result('demo-2', 'Adversarial', 'pass')] });
    assert.equal(report.verdict, 'PASS');
    assert.equal(report.partial, false);
  });

  test('FAIL when a threshold is missed, with the failing ids in the markdown', () => {
    const report = buildReport({ ...base, caseResults: [result('demo-1', 'Happy path', 'pass'), result('demo-2', 'Adversarial', 'fail')] });
    assert.equal(report.verdict, 'FAIL');
    assert.match(renderMarkdown(report), /demo-2 \(Adversarial\): fail/);
  });

  test('NOT RUN when any case could not reach the model, even with every evaluated case passing', () => {
    const report = buildReport({ ...base, caseResults: [result('demo-1', 'Happy path', 'pass'), result('demo-2', 'Adversarial', 'not_run', { reason: 'service answered 503 ai_unavailable' })] });
    assert.equal(report.verdict, 'NOT RUN');
    assert.deepEqual(report.notRunReasons, ['service answered 503 ai_unavailable']);
  });

  test('NOT GATED without a threshold entry, and partial when fewer cases ran than the dataset holds', () => {
    const report = buildReport({ ...base, thresholdEntry: undefined, datasetTotal: 5, caseResults: [result('demo-1', 'Happy path', 'pass')] });
    assert.equal(report.verdict, 'NOT GATED');
    assert.equal(report.partial, true);
    assert.equal(report.thresholds, null);
  });

  test('never includes anything but ids, classes, statuses and sanitised check details', () => {
    const leak = result('demo-1', 'Happy path', 'fail', {
      checks: [{ name: 'c', pass: false, detail: 'echo sk-ant-SECRETVALUE123 and body' }],
    });
    const report = buildReport({ ...base, secrets: [], caseResults: [leak, result('demo-2', 'Adversarial', 'pass')] });
    const text = JSON.stringify(report);
    assert.ok(!text.includes('SECRETVALUE123'));
    assert.deepEqual(Object.keys(report.cases[0]).sort(), ['checks', 'class', 'id', 'latencyMs', 'status', 'tokensEstimated']);
    assert.equal(report.modelId, 'claude-haiku-5-5');
    assert.equal(report.tokens.estimated, true);
  });
});

describe('writeReport and stampFor', () => {
  test('writes a .json and a .md named by feature and stamp', () => {
    const outDir = join(mkdtempSync(join(tmpdir(), 'report-test-')), 'out');
    const report = buildReport({
      feature: 'demo', target: 'api', baseHost: null, startedAt: 'a', finishedAt: 'b', modelId: null, promptVersion: null,
      budget: new TokenBudget(null), datasetTotal: 1, caseResults: [result('demo-1', 'Happy path', 'pass')], thresholdEntry: undefined,
    });
    const stamp = stampFor(new Date('2026-10-11T16:05:09.123Z'));
    assert.equal(stamp, '20261011T160509Z');
    const { jsonPath, mdPath } = writeReport({ outDir, feature: 'demo', stamp, report });
    assert.ok(existsSync(jsonPath) && existsSync(mdPath));
    assert.equal(JSON.parse(readFileSync(jsonPath, 'utf8')).feature, 'demo');
    assert.match(readFileSync(mdPath, 'utf8'), /^# demo: NOT GATED/);
  });
});
