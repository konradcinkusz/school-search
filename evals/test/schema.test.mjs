import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { ID_PATTERN, MIN_CASES, listDatasetFeatures, loadDataset, parseDatasetText, validateCase, validateDataset, validateUniqueIds } from '../lib/schema.mjs';
import { casesToJsonl, makeCases } from './helpers.mjs';

const good = () => ({ id: 'demo-001', class: 'Happy path', input: { text: 'a' }, expect: {}, notes: 'n' });

describe('validateCase', () => {
  test('accepts a valid case and a synthetic PII case', () => {
    assert.deepEqual(validateCase(good(), { feature: 'demo' }), []);
    const pii = { ...good(), class: 'Adversarial', input: { text: 'mail jan@example.com' }, synthetic_pii: true };
    assert.deepEqual(validateCase(pii, { feature: 'demo' }), []);
  });

  test('rejects unknown keys, bad ids, wrong feature prefix and classes outside AI-EVALS section 2', () => {
    assert.ok(validateCase({ ...good(), syntheticPii: true }).some((e) => e.includes('unknown key')));
    assert.ok(validateCase({ ...good(), id: 'demo-1' }).some((e) => e.includes('<feature>-<nnn>')));
    assert.ok(validateCase(good(), { feature: 'other' }).some((e) => e.includes('does not start with feature other')));
    assert.ok(validateCase({ ...good(), class: 'Happy' }).some((e) => e.includes('class must be one of')));
    assert.ok(validateCase({ ...good(), class: 'Mutation' }).length > 0);
  });

  test('requires object input and expect, string notes, boolean synthetic_pii', () => {
    assert.ok(validateCase({ ...good(), input: [] }).some((e) => e.includes('input must be an object')));
    assert.ok(validateCase({ ...good(), expect: 'x' }).some((e) => e.includes('expect must be an object')));
    assert.ok(validateCase({ ...good(), notes: undefined }).some((e) => e.includes('notes must be a string')));
    assert.ok(validateCase({ ...good(), synthetic_pii: 'yes' }).some((e) => e.includes('boolean')));
    assert.ok(validateCase(['x']).length === 1);
  });

  test('flags PII in any string unless synthetic_pii, and never echoes the value', () => {
    const errors = validateCase({ ...good(), input: { text: 'napisz do jan@example.com' } });
    assert.equal(errors.length, 1);
    assert.ok(errors[0].includes('$.input.text looks like email'));
    assert.ok(!errors[0].includes('jan@example.com'));
  });

  test('ID_PATTERN captures the feature part', () => {
    assert.equal(ID_PATTERN.exec('search-intent-012')[1], 'search-intent');
    assert.equal(ID_PATTERN.exec('demo-1234'), null);
  });
});

describe('parseDatasetText', () => {
  test('returns valid cases and errors with line numbers, never the content', () => {
    const text = `${JSON.stringify(good())}\n\n{"id":"demo-002"}\nnot json jan@example.com\n`;
    const parsed = parseDatasetText(text, { feature: 'demo' });
    assert.equal(parsed.cases.length, 1);
    assert.ok(parsed.errors.some((e) => e.startsWith('demo/cases.jsonl:2: blank line')));
    assert.ok(parsed.errors.some((e) => e.startsWith('demo/cases.jsonl:4: not valid JSON')));
    assert.ok(parsed.errors.every((e) => !e.includes('jan@example.com')));
  });

  test('rejects a byte order mark and accepts CRLF line endings', () => {
    assert.match(parseDatasetText('﻿{}', { feature: 'demo' }).errors[0], /byte order mark/);
    const crlf = `${JSON.stringify(good())}\r\n`;
    assert.equal(parseDatasetText(crlf, { feature: 'demo' }).errors.length, 0);
  });
});

describe('validateDataset', () => {
  test('requires the minimum size, unique ids and the required classes', () => {
    const short = makeCases('demo', 10);
    assert.ok(validateDataset('demo', short).some((e) => e.includes(`at least ${MIN_CASES} required`)));
    const dup = makeCases('demo', 40);
    dup[5] = { ...dup[5], id: dup[4].id };
    assert.ok(validateDataset('demo', dup).some((e) => e.includes('duplicate case id demo-005')));
    const noDegrade = makeCases('demo', 40).map((c) => (c.class === 'Degradation' ? { ...c, class: 'Ambiguity' } : c));
    assert.ok(validateDataset('demo', noDegrade).some((e) => e.includes('"Degradation"')));
    assert.deepEqual(validateDataset('demo', makeCases('demo', 40)), []);
  });

  test('validateUniqueIds finds an id used in two datasets', () => {
    const a = makeCases('alpha', 40);
    const b = [{ ...makeCases('beta', 40)[0], id: 'alpha-001' }];
    assert.ok(validateUniqueIds([{ feature: 'alpha', cases: a }, { feature: 'beta', cases: b }])[0].includes('alpha-001'));
  });
});

describe('loadDataset and listDatasetFeatures', () => {
  function tree() {
    const root = mkdtempSync(join(tmpdir(), 'schema-test-'));
    mkdirSync(join(root, 'demo'), { recursive: true });
    mkdirSync(join(root, '_skip'), { recursive: true });
    writeFileSync(join(root, 'demo', 'cases.jsonl'), casesToJsonl(makeCases('demo')), 'utf8');
    writeFileSync(join(root, 'demo', 'README.md'), '# demo\n', 'utf8');
    return root;
  }

  test('loads a complete dataset and lists feature folders, skipping names starting with an underscore', () => {
    const root = tree();
    assert.deepEqual(listDatasetFeatures(root), ['demo']);
    const dataset = loadDataset(root, 'demo');
    assert.deepEqual(dataset.errors, []);
    assert.equal(dataset.cases.length, 40);
  });

  test('reports a missing README and a missing cases file', () => {
    const root = tree();
    rmSync(join(root, 'demo', 'README.md'));
    assert.ok(loadDataset(root, 'demo').errors.includes('demo/README.md: missing'));
    assert.deepEqual(loadDataset(root, 'absent').errors, ['absent/cases.jsonl: missing']);
    assert.deepEqual(listDatasetFeatures(join(root, 'nope')), []);
  });
});
