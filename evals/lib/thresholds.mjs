// Thresholds from evals/thresholds.json. Every entry is a PROPOSAL until "decided_by_owner" is true.
import { existsSync, readFileSync } from 'node:fs';
import { ConfigError } from './errors.mjs';
import { SCENARIO_CLASSES, isPlainObject } from './schema.mjs';

const EPSILON = 1e-9;
const ENTRY_KEYS = Object.freeze(['decided_by_owner', 'source', 'overall', 'perClass', 'note']);

const isRate = (value) => typeof value === 'number' && value >= 0 && value <= 1;

// Reads and validates evals/thresholds.json. A missing file is an empty object; a broken one is a ConfigError.
export function loadThresholdsFile(file) {
  if (!existsSync(file)) return {};
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(file, 'utf8'));
  } catch {
    throw new ConfigError('thresholds.json is not valid JSON');
  }
  const errors = validateThresholds(parsed);
  if (errors.length > 0) throw new ConfigError(errors.join('; '));
  return parsed;
}

// Returns a list of problems in the parsed thresholds file. Keys starting with "$" are comments.
export function validateThresholds(thresholds) {
  if (!isPlainObject(thresholds)) return ['thresholds.json must be an object keyed by feature name'];
  const errors = [];
  for (const [feature, entry] of Object.entries(thresholds)) {
    if (feature.startsWith('$')) continue;
    const label = `thresholds.json ${feature}`;
    if (!isPlainObject(entry)) {
      errors.push(`${label}: must be an object`);
      continue;
    }
    for (const key of Object.keys(entry)) {
      if (!ENTRY_KEYS.includes(key)) errors.push(`${label}: unknown key "${key}"`);
    }
    if (typeof entry.decided_by_owner !== 'boolean') errors.push(`${label}: decided_by_owner must be a boolean`);
    if (typeof entry.source !== 'string' || entry.source === '') errors.push(`${label}: source is required`);
    if (entry.overall === undefined && entry.perClass === undefined) {
      errors.push(`${label}: give overall or perClass`);
    }
    if (entry.overall !== undefined && !isRate(entry.overall)) errors.push(`${label}: overall must be 0..1`);
    if (entry.perClass !== undefined) {
      if (!isPlainObject(entry.perClass)) {
        errors.push(`${label}: perClass must be an object`);
      } else {
        for (const [scenarioClass, rate] of Object.entries(entry.perClass)) {
          if (!SCENARIO_CLASSES.includes(scenarioClass)) errors.push(`${label}: unknown class "${scenarioClass}"`);
          if (!isRate(rate)) errors.push(`${label}: perClass["${scenarioClass}"] must be 0..1`);
        }
      }
    }
  }
  return errors;
}

// Compares a summary (from report.mjs summarize) with one thresholds entry.
// pass === null means "not evaluated" (a class with no evaluated case in a partial run).
export function evaluateThresholds(summary, entry, { partial = false } = {}) {
  const checks = [];
  if (entry.overall !== undefined) {
    const actual = summary.overallRate;
    checks.push({
      name: 'overall',
      threshold: entry.overall,
      actual,
      pass: actual !== null && actual + EPSILON >= entry.overall,
    });
  }
  for (const [scenarioClass, threshold] of Object.entries(entry.perClass ?? {})) {
    const stats = summary.perClass[scenarioClass];
    if (!stats || stats.evaluated === 0) {
      checks.push({
        name: scenarioClass,
        threshold,
        actual: null,
        pass: partial ? null : false,
        detail: 'no evaluated case in this class',
      });
    } else {
      checks.push({
        name: scenarioClass,
        threshold,
        actual: stats.rate,
        pass: stats.rate + EPSILON >= threshold,
      });
    }
  }
  return { failed: checks.some((check) => check.pass === false), checks };
}

// NOT RUN beats everything: a run that could not reach the model must never read as PASS.
export function decideVerdict({ notRun, hasEntry, thresholdFailed }) {
  if (notRun) return 'NOT RUN';
  if (!hasEntry) return 'NOT GATED';
  return thresholdFailed ? 'FAIL' : 'PASS';
}
