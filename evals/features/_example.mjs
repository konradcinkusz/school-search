// Stub feature module: shows the contract. Files starting with "_" are never discovered, so this is not run.
// A real module (explain.mjs, search-intent.mjs, methodology.mjs, agent.mjs) follows the same shape.
import { registerGrader } from '../lib/registry.mjs';

export const feature = 'example';
export const target = 'api';

export function buildRequest(evalCase) {
  return { method: 'POST', path: '/api/v1/ai/example', body: { text: evalCase.input.text } };
}

// Grader: must return { pass, checks }. pass is derived from the checks by the harness; it must agree with them.
export function grade(evalCase, response) {
  const checks = [
    {
      name: 'status-is-200-or-503',
      pass: response.status === 200 || response.status === 503,
      detail: `status ${response.status}`,
    },
  ];
  return { pass: checks.every((check) => check.pass), checks };
}

registerGrader(feature, grade);
