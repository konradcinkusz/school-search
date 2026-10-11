// Offline check, run by CI. No network and no key. It validates every dataset, every thresholds entry, every feature
// module, runs buildRequest over every case, and runs each graders against its recorded fixtures.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { discoverFeatures, validateFeatureModule } from './features.mjs';
import { findPii, stringLeaves } from './pii.mjs';
import { grade } from './registry.mjs';
import { validateRequest } from './runner.mjs';
import { isPlainObject, listDatasetFeatures, loadDataset, validateUniqueIds } from './schema.mjs';
import { validateThresholds } from './thresholds.mjs';

const FIXTURE_KEYS = Object.freeze(['caseId', 'shouldPass', 'response', 'note', 'synthetic_pii']);

// Problems with one fixture file. The file is {caseId, shouldPass, response: {status, body}, note?, synthetic_pii?}.
function validateFixture(fixture, label) {
  const errors = [];
  if (!isPlainObject(fixture)) return [`${label}: must be a JSON object`];
  for (const key of Object.keys(fixture)) {
    if (!FIXTURE_KEYS.includes(key)) errors.push(`${label}: unknown key "${key}"`);
  }
  if (typeof fixture.caseId !== 'string' || fixture.caseId === '') errors.push(`${label}: caseId is required`);
  if (typeof fixture.shouldPass !== 'boolean') errors.push(`${label}: shouldPass must be a boolean`);
  if (!isPlainObject(fixture.response) || !Number.isInteger(fixture.response.status)) {
    errors.push(`${label}: response must be { status: integer, body }`);
  }
  if (fixture.synthetic_pii !== undefined && typeof fixture.synthetic_pii !== 'boolean') {
    errors.push(`${label}: synthetic_pii must be a boolean`);
  }
  if (fixture.synthetic_pii !== true) {
    for (const { path, text } of stringLeaves(fixture)) {
      const kinds = findPii(text);
      if (kinds.length > 0) errors.push(`${label}: ${path} looks like ${kinds.join('/')} (value not shown)`);
    }
  }
  return errors;
}

// Runs the graders on the recorded fixtures of one feature. Each grader must pass its shouldPass:true fixtures,
// fail its shouldPass:false fixtures, and give the same answer twice.
async function checkFixtures({ name, dir, cases, errors }) {
  const counts = { pass: 0, fail: 0 };
  if (!existsSync(dir)) {
    errors.push(`fixtures/${name}: directory missing`);
    return counts;
  }
  const byId = new Map(cases.map((evalCase) => [evalCase.id, evalCase]));
  const files = readdirSync(dir).filter((file) => file.endsWith('.json')).sort();
  for (const file of files) {
    const label = `fixtures/${name}/${file}`;
    let fixture;
    try {
      fixture = JSON.parse(readFileSync(join(dir, file), 'utf8'));
    } catch {
      errors.push(`${label}: not valid JSON`);
      continue;
    }
    const shapeErrors = validateFixture(fixture, label);
    errors.push(...shapeErrors);
    if (shapeErrors.length > 0) continue;
    const evalCase = byId.get(fixture.caseId);
    if (!evalCase) {
      errors.push(`${label}: caseId ${fixture.caseId} is not in the ${name} dataset`);
      continue;
    }
    try {
      const first = await grade(name, evalCase, fixture.response);
      const second = await grade(name, evalCase, fixture.response);
      if (JSON.stringify(first) !== JSON.stringify(second)) {
        errors.push(`${label}: grader gave different answers on two runs`);
      }
      if (first.pass !== fixture.shouldPass) {
        const failing = first.checks.filter((check) => !check.pass).map((check) => check.name);
        errors.push(`${label}: grader pass=${first.pass}, shouldPass=${fixture.shouldPass}`
          + (failing.length ? ` (failing checks: ${failing.join(', ')})` : ''));
      }
      counts[fixture.shouldPass ? 'pass' : 'fail'] += 1;
    } catch (error) {
      errors.push(`${label}: grader error: ${error.message}`);
    }
  }
  if (counts.pass === 0) errors.push(`fixtures/${name}: no fixture with shouldPass true`);
  if (counts.fail === 0) errors.push(`fixtures/${name}: no fixture with shouldPass false (show the grader can fail)`);
  return counts;
}

// paths: { datasetsDir, fixturesDir, featuresDir, thresholdsFile }
// Datasets graded only by the .NET project tests/SchoolSearch.Evals.Tests (they call product code, not HTTP).
const TIER_B_ONLY = new Set(['review-triage', 'correction-triage', 'profile-copy', 'catchment-extract', 'preschool-extract']);

export async function runSelftest(paths) {
  const errors = [];
  const warnings = [];
  const summary = [];

  let thresholds = {};
  if (!existsSync(paths.thresholdsFile)) {
    errors.push('thresholds.json: missing');
  } else {
    try {
      thresholds = JSON.parse(readFileSync(paths.thresholdsFile, 'utf8'));
      errors.push(...validateThresholds(thresholds));
    } catch {
      errors.push('thresholds.json: not valid JSON');
    }
  }

  const discovered = await discoverFeatures(paths.featuresDir);
  errors.push(...discovered.errors);
  const modules = new Map();
  for (const { name, module } of discovered.modules) {
    errors.push(...validateFeatureModule(name, module));
    modules.set(name, module);
  }

  const datasetNames = listDatasetFeatures(paths.datasetsDir);
  const datasets = [];
  for (const name of datasetNames) {
    const dataset = loadDataset(paths.datasetsDir, name);
    errors.push(...dataset.errors);
    datasets.push(dataset);
    const module = modules.get(name);
    if (!module && TIER_B_ONLY.has(name)) {
      console.log(`${name}: ${dataset.cases.length} cases (graded by tests/SchoolSearch.Evals.Tests, no Node module)`);
      continue;
    }
    if (!module) {
      errors.push(`${name}: dataset without a feature module (evals/features/${name}.mjs)`);
      continue;
    }
    for (const evalCase of dataset.cases) {
      try {
        validateRequest(module.buildRequest(evalCase));
      } catch (error) {
        errors.push(`${name}: buildRequest failed for ${evalCase.id}: ${error.message}`);
      }
    }
    const counts = await checkFixtures({
      name,
      dir: join(paths.fixturesDir, name),
      cases: dataset.cases,
      errors,
    });
    if (!thresholds[name]) warnings.push(`${name}: no entry in thresholds.json (not gated)`);
    const classCounts = {};
    for (const evalCase of dataset.cases) classCounts[evalCase.class] = (classCounts[evalCase.class] ?? 0) + 1;
    summary.push({ feature: name, cases: dataset.cases.length, classes: classCounts, fixturesPass: counts.pass, fixturesFail: counts.fail });
  }

  for (const name of modules.keys()) {
    if (!datasetNames.includes(name)) errors.push(`${name}: feature module without a dataset (evals/datasets/${name}/)`);
  }
  errors.push(...validateUniqueIds(datasets.map((d) => ({ feature: d.feature, cases: d.cases }))));
  for (const name of Object.keys(thresholds)) {
    if (!name.startsWith('$') && !datasetNames.includes(name)) warnings.push(`thresholds.json: ${name} has no dataset yet`);
  }

  return { ok: errors.length === 0, errors, warnings, summary };
}
