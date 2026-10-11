import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { parseArgs } from '../lib/args.mjs';
import { ConfigError } from '../lib/errors.mjs';

describe('parseArgs', () => {
  test('reads the flags and a comma-separated feature list', () => {
    assert.deepEqual(parseArgs(['--live', '--feature', 'explain, search-intent']), {
      selftest: false, live: true, features: ['explain', 'search-intent'], help: false,
    });
    assert.equal(parseArgs(['--selftest']).selftest, true);
    assert.equal(parseArgs(['-h']).help, true);
  });

  test('rejects unknown arguments and a missing feature value', () => {
    assert.throws(() => parseArgs(['--yes']), ConfigError);
    assert.throws(() => parseArgs(['--feature']), /needs a value/);
    assert.throws(() => parseArgs(['--feature', '--live']), /needs a value/);
  });
});
