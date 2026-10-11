import assert from 'node:assert/strict';
import { writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { beforeEach, describe, test } from 'node:test';
import { clearGraders } from '../lib/registry.mjs';
import { runSelftest } from '../lib/selftest.mjs';
import { makeTree } from './helpers.mjs';

describe('runSelftest', () => {
  beforeEach(() => clearGraders());
  test('passes on a complete tree with a grader that passes good and fails bad fixtures', async () => {
    const paths = makeTree(['st-ok'], { thresholds: { 'st-ok': { decided_by_owner: false, source: 's', overall: 1 } } });
    const result = await runSelftest(paths);
    assert.deepEqual(result.errors, []);
    assert.equal(result.ok, true);
    assert.equal(result.summary[0].fixturesPass, 1);
    assert.equal(result.summary[0].fixturesFail, 1);
    assert.deepEqual(result.warnings, []);
  });

  test('warns, but does not fail, when a dataset has no thresholds entry', async () => {
    const paths = makeTree(['st-warn']);
    const result = await runSelftest(paths);
    assert.equal(result.ok, true);
    assert.ok(result.warnings.some((w) => w.includes('not gated')));
  });

  test('fails when a fixture disagrees with its grader', async () => {
    const paths = makeTree(['st-disagree'], { goodFixture: false });
    writeFileSync(join(paths.fixturesDir, 'st-disagree', 'bad.json'), JSON.stringify({
      caseId: 'st-disagree-002', shouldPass: false, response: { status: 200, body: { answer: 'fine' } },
    }));
    const result = await runSelftest(paths);
    assert.equal(result.ok, false);
    assert.ok(result.errors.some((e) => e.includes('grader pass=true, shouldPass=false')));
  });

  test('fails when a feature has no fixture that the grader must fail, or no fixtures folder', async () => {
    const paths = makeTree(['st-nofail']);
    rmSync(join(paths.fixturesDir, 'st-nofail', 'bad.json'));
    const result = await runSelftest(paths);
    assert.ok(result.errors.some((e) => e.includes('no fixture with shouldPass false')));
    rmSync(join(paths.fixturesDir, 'st-nofail'), { recursive: true });
    const again = await runSelftest(paths);
    assert.ok(again.errors.some((e) => e.includes('directory missing')));
  });

  test('fails for a dataset without a module and a module without a dataset', async () => {
    const paths = makeTree(['st-orphan']);
    rmSync(join(paths.featuresDir, 'st-orphan.mjs'));
    mkdirSync(join(paths.datasetsDir, 'st-empty-ds'), { recursive: true });
    writeFileSync(join(paths.featuresDir, 'st-lonely.mjs'), 'export const feature = "st-lonely";\n');
    const result = await runSelftest(paths);
    assert.ok(result.errors.some((e) => e.includes('st-orphan: dataset without a feature module')));
    assert.ok(result.errors.some((e) => e.includes('st-lonely.mjs')));
  });

  test('fails for a feature module that registers no grader', async () => {
    const paths = makeTree(['st-nograder'], { goodFixture: true });
    writeFileSync(join(paths.featuresDir, 'st-nograder.mjs'), `import { registerGrader } from ${JSON.stringify((await import('./helpers.mjs')).REGISTRY_URL)};
export const feature = 'st-nograder'; export const target = 'api'; export function buildRequest() { return { path: '/x' }; }\n`);
    const result = await runSelftest(paths);
    assert.ok(result.errors.some((e) => e.includes('no grader registered')));
  });

  test('fails on a broken thresholds file and on PII in a fixture', async () => {
    const paths = makeTree(['st-pii']);
    writeFileSync(paths.thresholdsFile, '{"st-pii": {"source": "x"}}');
    writeFileSync(join(paths.fixturesDir, 'st-pii', 'pii.json'), JSON.stringify({
      caseId: 'st-pii-001', shouldPass: true, response: { status: 200, body: { mail: 'jan@example.com' } },
    }));
    const result = await runSelftest(paths);
    assert.ok(result.errors.some((e) => e.includes('decided_by_owner')));
    assert.ok(result.errors.some((e) => e.includes('looks like email (value not shown)')));
    assert.ok(result.errors.every((e) => !e.includes('jan@example.com')));
  });
});
