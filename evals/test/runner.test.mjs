import assert from 'node:assert/strict';
import { beforeEach, describe, test } from 'node:test';
import { clearGraders, registerGrader } from '../lib/registry.mjs';
import { TokenBudget } from '../lib/http.mjs';
import { runCases, validateRequest } from '../lib/runner.mjs';
import { fakeFetch, makeCases } from './helpers.mjs';

const build = (c) => ({ method: 'POST', path: '/x', body: { text: c.input.text } });
const grader = (evalCase, response) => {
  const checks = [{ name: 'status-200', pass: response.status === 200, detail: String(response.status) }];
  return { pass: checks[0].pass, checks };
};

beforeEach(() => {
  clearGraders();
  registerGrader('runner-demo', grader);
});

function run(overrides) {
  return runCases({
    feature: 'runner-demo',
    cases: makeCases('demo', 40).slice(0, 3),
    buildRequest: build,
    base: 'https://api.test',
    timeoutMs: 1000,
    paceMs: 0,
    budget: new TokenBudget(null),
    sleep: async () => {},
    ...overrides,
  });
}

describe('validateRequest', () => {
  test('defaults the method to POST and rejects bad requests', () => {
    assert.equal(validateRequest({ path: '/a' }).method, 'POST');
    assert.throws(() => validateRequest(null), /buildRequest must return/);
    assert.throws(() => validateRequest({ method: 'PUT', path: '/a' }), /GET or POST/);
    assert.throws(() => validateRequest({ path: 'a' }), /start with \//);
    assert.throws(() => validateRequest({ path: '/a', headers: 'x' }), /headers/);
  });
});

describe('runCases', () => {
  test('grades each answer and marks pass and fail', async () => {
    let n = 0;
    const { fetchImpl } = fakeFetch(() => { n += 1; return { status: n === 2 ? 500 : 200, body: {} }; });
    const results = await run({ fetchImpl });
    assert.deepEqual(results.map((r) => r.status), ['pass', 'fail', 'pass']);
    assert.equal(results[1].checks[0].name, 'status-200');
  });

  test('a 503 ai_unavailable outside Degradation is NOT RUN and the grader is not called', async () => {
    const { fetchImpl } = fakeFetch(() => ({ status: 503, body: { error: { code: 'ai_unavailable', message: 'x' } } }));
    const results = await run({ fetchImpl });
    assert.ok(results.every((r) => r.status === 'not_run'));
    assert.equal(results[0].reason, 'service answered 503 ai_unavailable');
  });

  test('ai_budget_exhausted is NOT RUN too, but in the Degradation class the 503 is graded', async () => {
    const { fetchImpl } = fakeFetch(() => ({ status: 503, body: { error: { code: 'ai_budget_exhausted' } } }));
    const degrade = [{ ...makeCases('demo')[0], class: 'Degradation', id: 'demo-900' }];
    const results = await run({ fetchImpl, cases: degrade });
    assert.equal(results[0].status, 'fail');
    const normal = await run({ fetchImpl, cases: makeCases('demo').slice(0, 1) });
    assert.equal(normal[0].status, 'not_run');
  });

  test('a missing base URL makes every case NOT RUN without any call', async () => {
    const { fetchImpl, calls } = fakeFetch(() => ({ status: 200, body: {} }));
    const results = await run({ fetchImpl, base: null });
    assert.ok(results.every((r) => r.status === 'not_run' && r.reason === 'base URL not set'));
    assert.equal(calls.length, 0);
  });

  test('an exhausted token budget stops further calls and marks the rest NOT RUN', async () => {
    const { fetchImpl, calls } = fakeFetch(() => ({ status: 200, body: {} }));
    const budget = new TokenBudget(1);
    budget.record(5);
    const results = await run({ fetchImpl, budget });
    assert.equal(calls.length, 0);
    assert.ok(results.every((r) => r.reason === 'token budget exhausted'));
  });

  test('a transport failure is a fail with a transport check, and the run goes on', async () => {
    let n = 0;
    const { fetchImpl } = fakeFetch(() => { n += 1; if (n === 1) throw new TypeError('x'); return { status: 200, body: {} }; });
    const results = await run({ fetchImpl });
    assert.equal(results[0].status, 'fail');
    assert.equal(results[0].reason, 'transport');
    assert.equal(results[0].checks[0].name, 'transport');
    assert.equal(results[1].status, 'pass');
  });

  test('a grader that throws is recorded as a failing grader-error check', async () => {
    clearGraders();
    registerGrader('runner-demo', () => { throw new Error('boom'); });
    const { fetchImpl } = fakeFetch(() => ({ status: 200, body: {} }));
    const results = await run({ fetchImpl, cases: makeCases('demo').slice(0, 1) });
    assert.equal(results[0].status, 'fail');
    assert.equal(results[0].checks[0].name, 'grader-error');
  });

  test('paces calls between cases but not before the first', async () => {
    const waits = [];
    const { fetchImpl } = fakeFetch(() => ({ status: 200, body: {} }));
    await run({ fetchImpl, paceMs: 250, sleep: async (ms) => waits.push(ms) });
    assert.deepEqual(waits, [250, 250]);
  });
});
