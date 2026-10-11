import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { beforeEach, describe, test } from 'node:test';
import { clearGraders } from '../lib/registry.mjs';
import { ConfigError } from '../lib/errors.mjs';
import { readLiveConfig, runLive } from '../lib/live.mjs';
import { fakeFetch, makeTree } from './helpers.mjs';

const thresholds = { 'lv-demo': { decided_by_owner: false, source: 'test', overall: 1 } };
const ok = () => ({ status: 200, body: { answer: 'fine' } });
const quiet = { log: () => {}, sleep: async () => {} };
const env = (extra = {}) => ({ EVAL_API_BASE: 'https://api.test', EVAL_PACE_MS: '0', ...extra });

describe('readLiveConfig', () => {
  test('defaults and validation of the EVAL_* variables', () => {
    const config = readLiveConfig({});
    assert.equal(config.apiBase, null);
    assert.equal(config.tokenBudget, 200000);
    assert.equal(config.maxCases, null);
    assert.equal(config.timeoutMs, 30000);
    assert.throws(() => readLiveConfig({ EVAL_MAX_CASES: '0' }), ConfigError);
    assert.throws(() => readLiveConfig({ EVAL_TOKEN_BUDGET: 'lots' }), /whole number/);
    assert.throws(() => readLiveConfig({ EVAL_API_BASE: 'ftp://x' }), ConfigError);
  });
});

describe('runLive', () => {
  beforeEach(() => clearGraders());
  test('PASS when every answer passes, and writes both report files', async () => {
    const paths = makeTree(['lv-demo'], { thresholds });
    const { fetchImpl, calls } = fakeFetch(ok);
    const outcome = await runLive({ paths, env: env(), fetchImpl, ...quiet });
    assert.equal(outcome.exitCode, 0);
    assert.equal(outcome.outcomes[0].verdict, 'PASS');
    assert.equal(calls.length, 40);
    assert.equal(calls[0].url, 'https://api.test/api/v1/ai/lv-demo');
    const files = readdirSync(paths.outDir).sort();
    assert.equal(files.length, 2);
    assert.ok(files[0].startsWith('lv-demo-') && files.some((f) => f.endsWith('.md')));
    const report = JSON.parse(readFileSync(join(paths.outDir, files.find((f) => f.endsWith('.json'))), 'utf8'));
    assert.equal(report.baseHost, 'api.test');
    assert.equal(report.summary.passed, 40);
    assert.ok(!JSON.stringify(report).includes('fine'), 'reports never carry answer bodies');
  });

  test('FAIL exits 1 when a threshold is missed', async () => {
    const paths = makeTree(['lv-demo'], { thresholds });
    let n = 0;
    const { fetchImpl } = fakeFetch(() => { n += 1; return n === 1 ? { status: 500, body: {} } : ok(); });
    const outcome = await runLive({ paths, env: env(), fetchImpl, ...quiet });
    assert.equal(outcome.outcomes[0].verdict, 'FAIL');
    assert.equal(outcome.exitCode, 1);
  });

  test('a service without a key answers 503 ai_unavailable: NOT RUN, never PASS', async () => {
    const paths = makeTree(['lv-demo'], { thresholds });
    const { fetchImpl } = fakeFetch(() => ({ status: 503, body: { error: { code: 'ai_unavailable', message: 'x' } } }));
    const outcome = await runLive({ paths, env: env(), fetchImpl, ...quiet });
    assert.equal(outcome.outcomes[0].verdict, 'NOT RUN');
    assert.equal(outcome.exitCode, 0);
  });

  test('an unset base URL is NOT RUN with no network call', async () => {
    const paths = makeTree(['lv-demo'], { thresholds });
    const { fetchImpl, calls } = fakeFetch(ok);
    const outcome = await runLive({ paths, env: { EVAL_PACE_MS: '0' }, fetchImpl, ...quiet });
    assert.equal(outcome.outcomes[0].verdict, 'NOT RUN');
    assert.equal(calls.length, 0);
  });

  test('EVAL_MAX_CASES gives a partial run; the verdict is still computed from what ran', async () => {
    const paths = makeTree(['lv-demo'], { thresholds });
    const { fetchImpl, calls } = fakeFetch(ok);
    const outcome = await runLive({ paths, env: env({ EVAL_MAX_CASES: '5' }), fetchImpl, ...quiet });
    assert.equal(calls.length, 5);
    assert.equal(outcome.outcomes[0].partial, true);
    assert.equal(outcome.outcomes[0].verdict, 'PASS');
  });

  test('an exhausted token budget leaves the rest NOT RUN', async () => {
    const paths = makeTree(['lv-demo'], { thresholds });
    const { fetchImpl, calls } = fakeFetch(ok);
    const outcome = await runLive({ paths, env: env({ EVAL_TOKEN_BUDGET: '1' }), fetchImpl, ...quiet });
    assert.equal(calls.length, 1);
    assert.equal(outcome.outcomes[0].verdict, 'NOT RUN');
  });

  test('an unknown feature selection and an invalid dataset are configuration errors before any call', async () => {
    const paths = makeTree(['lv-demo'], { thresholds });
    const { fetchImpl, calls } = fakeFetch(ok);
    await assert.rejects(() => runLive({ paths, selected: ['nope'], env: env(), fetchImpl, ...quiet }), /no feature module/);
    assert.equal(calls.length, 0);
    rmDataset(paths, 'lv-demo');
    await assert.rejects(() => runLive({ paths, env: env(), fetchImpl, ...quiet }), ConfigError);
    assert.equal(calls.length, 0);
    assert.equal(existsSync(paths.outDir), true);
  });
});

function rmDataset(paths, feature) {
  // Removing the README makes the dataset invalid; the run must refuse before calling anything.
  import('node:fs').then((fs) => fs.rmSync(join(paths.datasetsDir, feature, 'README.md')));
}
