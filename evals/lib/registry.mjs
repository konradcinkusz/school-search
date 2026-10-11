// Grader registry. A feature module registers ONE grader at import time:
//
//   registerGrader(feature, fn)
//     feature: string, lower-case, matches the dataset folder and the feature module file name.
//     fn(evalCase, response) -> GradeResult | Promise<GradeResult>
//       evalCase: one object from evals/datasets/<feature>/cases.jsonl (id, class, input, expect, notes, synthetic_pii?)
//       response: { status: number, body: unknown }   (body is parsed JSON, or the raw text when it is not JSON)
//       GradeResult: { pass: boolean, checks: [{ name: string, pass: boolean, detail: string }] }
//         pass must equal "every check passed"; a grader that disagrees with its own checks is rejected.
//
// Graders must be deterministic and must not read latencyMs or anything outside (evalCase, response).

const FEATURE_NAME = /^[a-z][a-z0-9-]*$/;
const graders = new Map();

export function registerGrader(feature, fn) {
  if (typeof feature !== 'string' || !FEATURE_NAME.test(feature)) {
    throw new TypeError(`registerGrader: invalid feature name ${JSON.stringify(feature)}`);
  }
  if (typeof fn !== 'function') {
    throw new TypeError(`registerGrader(${feature}): grader must be a function`);
  }
  if (graders.has(feature)) {
    throw new Error(`registerGrader(${feature}): a grader is already registered for this feature`);
  }
  graders.set(feature, fn);
  return fn;
}

export function getGrader(feature) {
  return graders.get(feature);
}

export function hasGrader(feature) {
  return graders.has(feature);
}

export function listGraders() {
  return [...graders.keys()].sort();
}

// Validates a grader's return value and returns a normalised copy. Throws TypeError on any problem.
export function normalizeGradeResult(result) {
  if (result === null || typeof result !== 'object') {
    throw new TypeError('grader result must be an object with pass and checks');
  }
  if (!Array.isArray(result.checks) || result.checks.length === 0) {
    throw new TypeError('grader result needs a non-empty checks array');
  }
  const checks = result.checks.map((check, index) => {
    if (check === null || typeof check !== 'object') {
      throw new TypeError(`check ${index} must be an object`);
    }
    if (typeof check.name !== 'string' || check.name === '') {
      throw new TypeError(`check ${index} needs a non-empty name`);
    }
    if (typeof check.pass !== 'boolean') {
      throw new TypeError(`check ${check.name} needs a boolean pass`);
    }
    return {
      name: check.name,
      pass: check.pass,
      detail: check.detail === undefined ? '' : String(check.detail),
    };
  });
  if (typeof result.pass !== 'boolean') {
    throw new TypeError('grader result needs a boolean pass');
  }
  const derived = checks.every((check) => check.pass);
  if (result.pass !== derived) {
    throw new TypeError('grader result: pass disagrees with the checks');
  }
  return { pass: derived, checks };
}

// Runs the registered grader for a feature and returns the normalised result. Throws if none is registered.
export async function grade(feature, evalCase, response) {
  const fn = graders.get(feature);
  if (!fn) {
    throw new Error(`no grader registered for feature ${feature}`);
  }
  return normalizeGradeResult(await fn(evalCase, response));
}

// Test helper only. Production code never clears the registry.
export function clearGraders() {
  graders.clear();
}
