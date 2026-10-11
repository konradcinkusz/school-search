// Dataset schema, validator and loader. One case per line in evals/datasets/<feature>/cases.jsonl:
//
//   {"id":"<feature>-<nnn>","class":"<one of SCENARIO_CLASSES>","input":{...},"expect":{...},"notes":"...",
//    "synthetic_pii":true}          <- synthetic_pii is optional and only for obviously fake PII patterns
//
// Errors are returned as strings and never echo case content (a PII hit is reported by kind only).
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { findPii, stringLeaves } from './pii.mjs';

// Exact names from docs/ai/AI-EVALS.md section 2. Do not add to this list without changing that section.
export const SCENARIO_CLASSES = Object.freeze(['Happy path', 'Ambiguity', 'Denied path', 'Adversarial', 'Degradation']);
// Every dataset must contain at least one case in each of these.
export const REQUIRED_CLASSES = Object.freeze(['Happy path', 'Adversarial', 'Degradation']);
// AI-FEATURES / AI-EVALS: every feature needs at least this many cases.
export const MIN_CASES = 40;
export const CASE_KEYS = Object.freeze(['id', 'class', 'input', 'expect', 'notes', 'synthetic_pii']);
export const ID_PATTERN = /^([a-z][a-z0-9-]*)-(\d{3})$/;

export function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

// Returns a list of problems with one parsed case. An empty list means the case is valid.
export function validateCase(raw, { feature } = {}) {
  if (!isPlainObject(raw)) {
    return ['case must be a JSON object'];
  }
  const errors = [];
  for (const key of Object.keys(raw)) {
    if (!CASE_KEYS.includes(key)) errors.push(`unknown key "${key}"`);
  }
  const { id, class: scenarioClass, input, expect, notes, synthetic_pii: syntheticPii } = raw;
  const match = typeof id === 'string' ? ID_PATTERN.exec(id) : null;
  if (!match) {
    errors.push('id must look like <feature>-<nnn>');
  } else if (feature !== undefined && match[1] !== feature) {
    errors.push(`id ${id} does not start with feature ${feature}`);
  }
  if (!SCENARIO_CLASSES.includes(scenarioClass)) {
    errors.push(`class must be one of: ${SCENARIO_CLASSES.join(', ')}`);
  }
  if (!isPlainObject(input)) errors.push('input must be an object');
  if (!isPlainObject(expect)) errors.push('expect must be an object');
  if (typeof notes !== 'string') errors.push('notes must be a string');
  if (syntheticPii !== undefined && typeof syntheticPii !== 'boolean') {
    errors.push('synthetic_pii must be a boolean');
  }
  if (syntheticPii !== true) {
    for (const { path, text } of stringLeaves(raw)) {
      const kinds = findPii(text);
      if (kinds.length > 0) {
        errors.push(`${path} looks like ${kinds.join('/')} (value not shown); use invented data or mark synthetic_pii`);
      }
    }
  }
  return errors;
}

// Parses the text of a cases.jsonl file. Returns the valid case objects and every error, with line numbers.
export function parseDatasetText(text, { feature, label = `${feature}/cases.jsonl` } = {}) {
  if (text.charCodeAt(0) === 0xfeff) {
    return { cases: [], errors: [`${label}: UTF-8 byte order mark is not allowed`] };
  }
  const lines = text.split('\n');
  if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
  const cases = [];
  const errors = [];
  lines.forEach((rawLine, index) => {
    const lineNo = index + 1;
    const line = rawLine.endsWith('\r') ? rawLine.slice(0, -1) : rawLine;
    if (line.trim() === '') {
      errors.push(`${label}:${lineNo}: blank line`);
      return;
    }
    let value;
    try {
      value = JSON.parse(line);
    } catch {
      // The parser message can quote the line, so it is not included.
      errors.push(`${label}:${lineNo}: not valid JSON`);
      return;
    }
    const caseErrors = validateCase(value, { feature });
    for (const message of caseErrors) errors.push(`${label}:${lineNo}: ${message}`);
    if (caseErrors.length === 0) cases.push(value);
  });
  return { cases, errors };
}

// Dataset-level checks: minimum size, unique ids, required classes.
export function validateDataset(feature, cases, { minCases = MIN_CASES } = {}) {
  const errors = [];
  if (cases.length < minCases) {
    errors.push(`${feature}: ${cases.length} valid cases, at least ${minCases} required`);
  }
  const seen = new Set();
  for (const evalCase of cases) {
    if (seen.has(evalCase.id)) errors.push(`${feature}: duplicate case id ${evalCase.id}`);
    seen.add(evalCase.id);
  }
  const classes = new Set(cases.map((evalCase) => evalCase.class));
  for (const required of REQUIRED_CLASSES) {
    if (!classes.has(required)) errors.push(`${feature}: no case in required class "${required}"`);
  }
  return errors;
}

// Case ids must be unique across every dataset, because reports and fixtures refer to them by id alone.
export function validateUniqueIds(datasets) {
  const owner = new Map();
  const errors = [];
  for (const { feature, cases } of datasets) {
    for (const evalCase of cases) {
      if (owner.has(evalCase.id)) {
        errors.push(`duplicate case id ${evalCase.id} in ${owner.get(evalCase.id)} and ${feature}`);
      } else {
        owner.set(evalCase.id, feature);
      }
    }
  }
  return errors;
}

// Loads and validates one dataset folder: cases.jsonl and README.md.
export function loadDataset(datasetsDir, feature) {
  const dir = join(datasetsDir, feature);
  const file = join(dir, 'cases.jsonl');
  const label = `${feature}/cases.jsonl`;
  if (!existsSync(file)) {
    return { feature, cases: [], errors: [`${label}: missing`] };
  }
  const parsed = parseDatasetText(readFileSync(file, 'utf8'), { feature, label });
  const errors = [...parsed.errors];
  if (!existsSync(join(dir, 'README.md'))) errors.push(`${feature}/README.md: missing`);
  errors.push(...validateDataset(feature, parsed.cases));
  return { feature, cases: parsed.cases, errors };
}

// Names of the dataset folders (ignores names starting with "_" or ".").
export function listDatasetFeatures(datasetsDir) {
  if (!existsSync(datasetsDir)) return [];
  return readdirSync(datasetsDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && !entry.name.startsWith('_') && !entry.name.startsWith('.'))
    .map((entry) => entry.name)
    .sort();
}
