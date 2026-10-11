import assert from 'node:assert/strict';
import { beforeEach, describe, test } from 'node:test';
import { clearGraders, getGrader, grade, hasGrader, listGraders, normalizeGradeResult, registerGrader } from '../lib/registry.mjs';
import { grade as exampleGrade, feature as exampleFeature } from '../features/_example.mjs';

beforeEach(() => clearGraders());

describe('registerGrader', () => {
  test('stores one grader per feature and returns it', () => {
    const fn = () => ({ pass: true, checks: [{ name: 'ok', pass: true, detail: '' }] });
    assert.equal(registerGrader('explain', fn), fn);
    assert.equal(getGrader('explain'), fn);
    assert.equal(hasGrader('explain'), true);
    assert.deepEqual(listGraders(), ['explain']);
  });

  test('rejects a second grader for the same feature', () => {
    registerGrader('explain', () => ({}));
    assert.throws(() => registerGrader('explain', () => ({})), /already registered/);
  });

  test('rejects an invalid feature name and a non-function', () => {
    assert.throws(() => registerGrader('Explain', () => ({})), /invalid feature name/);
    assert.throws(() => registerGrader('a_b', () => ({})), /invalid feature name/);
    assert.throws(() => registerGrader('', () => ({})), /invalid feature name/);
    assert.throws(() => registerGrader('explain', 'nope'), /must be a function/);
  });

  test('getGrader and hasGrader report absence', () => {
    assert.equal(getGrader('missing'), undefined);
    assert.equal(hasGrader('missing'), false);
  });
});

describe('normalizeGradeResult', () => {
  test('accepts a consistent result and normalises details to strings', () => {
    const result = normalizeGradeResult({ pass: true, checks: [{ name: 'a', pass: true }, { name: 'b', pass: true, detail: 3 }] });
    assert.deepEqual(result, {
      pass: true,
      checks: [{ name: 'a', pass: true, detail: '' }, { name: 'b', pass: true, detail: '3' }],
    });
  });

  test('derives a failing result and rejects a pass that disagrees with the checks', () => {
    assert.throws(() => normalizeGradeResult({ pass: true, checks: [{ name: 'a', pass: false }] }), /disagrees/);
    assert.equal(normalizeGradeResult({ pass: false, checks: [{ name: 'a', pass: false }] }).pass, false);
  });

  test('rejects missing or empty checks, bad check fields and non-boolean pass', () => {
    assert.throws(() => normalizeGradeResult(null), /object/);
    assert.throws(() => normalizeGradeResult({ pass: true, checks: [] }), /non-empty/);
    assert.throws(() => normalizeGradeResult({ pass: true, checks: [{ pass: true }] }), /needs a non-empty name/);
    assert.throws(() => normalizeGradeResult({ pass: true, checks: [{ name: 'a', pass: 'yes' }] }), /boolean pass/);
    assert.throws(() => normalizeGradeResult({ checks: [{ name: 'a', pass: true }] }), /boolean pass/);
  });
});

describe('grade', () => {
  test('runs the registered grader and normalises its result', async () => {
    registerGrader('explain', async (evalCase, response) => ({
      pass: response.status === 200,
      checks: [{ name: evalCase.id, pass: response.status === 200, detail: 'x' }],
    }));
    const result = await grade('explain', { id: 'explain-001' }, { status: 200, body: {} });
    assert.deepEqual(result, { pass: true, checks: [{ name: 'explain-001', pass: true, detail: 'x' }] });
  });

  test('throws when no grader is registered', async () => {
    await assert.rejects(() => grade('explain', {}, {}), /no grader registered/);
  });
});

describe('example stub', () => {
  test('is a valid module shape and its grader is pure', () => {
    assert.equal(exampleFeature, 'example');
    const checks = exampleGrade({ id: 'example-001' }, { status: 503, body: {} }).checks;
    assert.equal(checks[0].pass, true);
  });
});
