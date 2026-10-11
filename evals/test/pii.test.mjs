import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { findPii, looksLikePii, stringLeaves } from '../lib/pii.mjs';

describe('findPii', () => {
  test('detects e-mail, phone, PESEL and postal-code shapes', () => {
    assert.deepEqual(findPii('pisz na jan.kowalski@example.com'), ['email']);
    assert.deepEqual(findPii('tel. 512 345 678'), ['phone']);
    assert.deepEqual(findPii('tel +48 512-345-678'), ['phone']);
    assert.deepEqual(findPii('512345678'), ['phone']);
    assert.deepEqual(findPii('PESEL 44051401359'), ['pesel']);
    assert.deepEqual(findPii('ul. Testowa 1, 00-950 Miasto'), ['postal-code']);
  });

  test('does not flag ordinary numbers, dates, scores or TERYT codes', () => {
    for (const text of ['wynik 8,5 w 2026', 'data 2026-10-11', 'rok 2025', '1250,50 zł', 'gmina 9999999', 'kod 12-3']) {
      assert.equal(looksLikePii(text), false, text);
    }
  });

  test('stringLeaves walks nested strings with paths and skips keys', () => {
    const leaves = stringLeaves({ a: 'x', b: ['y', { c: 'z' }], d: 1 });
    assert.deepEqual(leaves.map((leaf) => [leaf.path, leaf.text]), [
      ['$.a', 'x'], ['$.b[0]', 'y'], ['$.b[1].c', 'z'],
    ]);
  });
});
