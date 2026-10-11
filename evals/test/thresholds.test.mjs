import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { ConfigError } from '../lib/errors.mjs';
import { decideVerdict, evaluateThresholds, loadThresholdsFile, validateThresholds } from '../lib/thresholds.mjs';
import { summarize } from '../lib/report.mjs';

const entry = { decided_by_owner: false, source: 'AI-EVALS test', overall: 0.9, perClass: { 'Happy path': 0.95 } };
const result = (scenarioClass, status) => ({ id: 'x', class: scenarioClass, status, checks: [] });

describe('validateThresholds', () => {
  test('accepts the proposal shape and skips "$" comment keys', () => {
    assert.deepEqual(validateThresholds({ $comment: 'x', demo: entry }), []);
  });

  test('rejects missing owner flag, source, rates out of range and unknown classes or keys', () => {
    const errors = validateThresholds({
      demo: { source: '', overall: 2, perClass: { Mutation: 0.5 }, extra: 1 },
    });
    assert.ok(errors.some((e) => e.includes('decided_by_owner')));
    assert.ok(errors.some((e) => e.includes('source is required')));
    assert.ok(errors.some((e) => e.includes('overall must be 0..1')));
    assert.ok(errors.some((e) => e.includes('unknown class "Mutation"')));
    assert.ok(errors.some((e) => e.includes('unknown key "extra"')));
    assert.ok(validateThresholds({ demo: { decided_by_owner: false, source: 's' } })[0].includes('give overall or perClass'));
    assert.ok(validateThresholds([]).length === 1);
  });
});

describe('evaluateThresholds and decideVerdict', () => {
  test('passes when every rate meets its threshold, and fails below it', () => {
    const summary = summarize([result('Happy path', 'pass'), result('Happy path', 'pass'), result('Adversarial', 'fail')]);
    const ok = evaluateThresholds(summary, { overall: 0.6, perClass: { 'Happy path': 1 } });
    assert.equal(ok.failed, false);
    const low = evaluateThresholds(summary, { overall: 0.9 });
    assert.equal(low.failed, true);
    assert.equal(low.checks[0].actual, 2 / 3);
  });

  test('a class with no evaluated case fails in a full run and is skipped in a partial one', () => {
    const summary = summarize([result('Happy path', 'pass')]);
    assert.equal(evaluateThresholds(summary, { perClass: { Adversarial: 1 } }).failed, true);
    const partial = evaluateThresholds(summary, { perClass: { Adversarial: 1 } }, { partial: true });
    assert.equal(partial.failed, false);
    assert.equal(partial.checks[0].pass, null);
  });

  test('NOT RUN beats every other verdict, and a missing entry is NOT GATED', () => {
    assert.equal(decideVerdict({ notRun: true, hasEntry: true, thresholdFailed: false }), 'NOT RUN');
    assert.equal(decideVerdict({ notRun: false, hasEntry: false, thresholdFailed: false }), 'NOT GATED');
    assert.equal(decideVerdict({ notRun: false, hasEntry: true, thresholdFailed: true }), 'FAIL');
    assert.equal(decideVerdict({ notRun: false, hasEntry: true, thresholdFailed: false }), 'PASS');
  });
});

describe('loadThresholdsFile', () => {
  test('a missing file is empty, a broken one is a ConfigError', () => {
    const dir = mkdtempSync(join(tmpdir(), 'thr-test-'));
    assert.deepEqual(loadThresholdsFile(join(dir, 'none.json')), {});
    const broken = join(dir, 'broken.json');
    writeFileSync(broken, '{nope');
    assert.throws(() => loadThresholdsFile(broken), ConfigError);
    const bad = join(dir, 'bad.json');
    writeFileSync(bad, JSON.stringify({ demo: { source: 's' } }));
    assert.throws(() => loadThresholdsFile(bad), ConfigError);
  });

  test('the committed evals/thresholds.json is valid and every entry is a proposal', async () => {
    const { defaultPaths } = await import('../lib/paths.mjs');
    const thresholds = loadThresholdsFile(defaultPaths().thresholdsFile);
    for (const [feature, value] of Object.entries(thresholds)) {
      if (feature.startsWith('$')) continue;
      assert.equal(value.decided_by_owner, false, feature);
    }
  });
});
