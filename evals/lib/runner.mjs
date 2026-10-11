// Runs one feature's cases against a live endpoint, one call at a time, and grades each answer.
import { AI_NOT_AVAILABLE_CODES, callEndpoint, estimateTokens, sleep as defaultSleep } from './http.mjs';
import { grade } from './registry.mjs';

// Checks what a feature module's buildRequest returned. Throws TypeError on a bad request.
export function validateRequest(request) {
  if (request === null || typeof request !== 'object') {
    throw new TypeError('buildRequest must return { method, path, body? }');
  }
  const method = request.method === undefined ? 'POST' : request.method;
  if (method !== 'GET' && method !== 'POST') {
    throw new TypeError('method must be GET or POST');
  }
  if (typeof request.path !== 'string' || !request.path.startsWith('/')) {
    throw new TypeError('path must start with /');
  }
  if (request.headers !== undefined && (request.headers === null || typeof request.headers !== 'object')) {
    throw new TypeError('headers must be an object of strings');
  }
  return { method, path: request.path, body: request.body, headers: request.headers ?? {} };
}

function resultFor(evalCase, fields) {
  return { id: evalCase.id, class: evalCase.class, checks: [], latencyMs: null, tokensEstimated: 0, ...fields };
}

// Returns one result per case, in order:
//   status: 'pass' | 'fail' | 'not_run'
//   A 503 ai_unavailable or ai_budget_exhausted answer is NOT RUN, except in the Degradation class, where the
//   503 is the behaviour under test and goes to the grader. Missing base URL and an exhausted token budget are NOT RUN.
export async function runCases({
  feature,
  cases,
  buildRequest,
  base,
  timeoutMs,
  paceMs,
  budget,
  fetchImpl,
  sleep = defaultSleep,
}) {
  const results = [];
  let calls = 0;
  for (const evalCase of cases) {
    if (!base) {
      results.push(resultFor(evalCase, { status: 'not_run', reason: 'base URL not set' }));
      continue;
    }
    if (evalCase.expect && evalCase.expect.offlineOnly === true) {
      results.push(resultFor(evalCase, { status: 'not_run', reason: 'offline-only degradation case' }));
      continue;
    }
    if (budget.exhausted) {
      results.push(resultFor(evalCase, { status: 'not_run', reason: 'token budget exhausted' }));
      continue;
    }
    const request = validateRequest(buildRequest(evalCase));
    if (calls > 0 && paceMs > 0) await sleep(paceMs);
    calls += 1;
    const requestTokens = estimateTokens(JSON.stringify(request.body ?? null));
    const started = Date.now();
    let response;
    try {
      response = await callEndpoint({
        base,
        path: request.path,
        method: request.method,
        body: request.body,
        headers: request.headers,
        timeoutMs,
        fetchImpl,
      });
    } catch (error) {
      budget.record(requestTokens);
      results.push(resultFor(evalCase, {
        status: 'fail',
        reason: 'transport',
        checks: [{ name: 'transport', pass: false, detail: error.message }],
        latencyMs: Date.now() - started,
        tokensEstimated: requestTokens,
      }));
      continue;
    }
    const latencyMs = Date.now() - started;
    const tokens = requestTokens + estimateTokens(JSON.stringify(response.body ?? null));
    budget.record(tokens);
    const code = response.body !== null && typeof response.body === 'object' && response.body.error
      ? response.body.error.code
      : undefined;
    if (response.status === 503 && AI_NOT_AVAILABLE_CODES.includes(code) && evalCase.class !== 'Degradation') {
      results.push(resultFor(evalCase, {
        status: 'not_run',
        reason: `service answered 503 ${code}`,
        latencyMs,
        tokensEstimated: tokens,
      }));
      continue;
    }
    if (evalCase.expect && evalCase.expect.degradeOnly === true && response.status === 200) {
      results.push(resultFor(evalCase, {
        status: 'not_run',
        reason: 'degradation case needs AI off or no key',
        latencyMs,
        tokensEstimated: tokens,
      }));
      continue;
    }
    let verdict;
    try {
      verdict = await grade(feature, evalCase, { status: response.status, body: response.body });
    } catch (error) {
      verdict = { pass: false, checks: [{ name: 'grader-error', pass: false, detail: error.message }] };
    }
    results.push(resultFor(evalCase, {
      status: verdict.pass ? 'pass' : 'fail',
      checks: verdict.checks,
      latencyMs,
      tokensEstimated: tokens,
    }));
  }
  return results;
}
