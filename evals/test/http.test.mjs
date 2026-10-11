import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { ConfigError } from '../lib/errors.mjs';
import { TokenBudget, callEndpoint, estimateTokens, parseBaseUrl } from '../lib/http.mjs';
import { fakeFetch } from './helpers.mjs';

describe('parseBaseUrl', () => {
  test('returns null when unset and strips trailing slashes', () => {
    assert.equal(parseBaseUrl(undefined, 'X'), null);
    assert.equal(parseBaseUrl('   ', 'X'), null);
    assert.equal(parseBaseUrl('https://api.example.test/', 'X'), 'https://api.example.test');
    assert.equal(parseBaseUrl('http://localhost:8080//', 'X'), 'http://localhost:8080');
  });

  test('refuses other schemes, credentials, queries and fragments', () => {
    assert.throws(() => parseBaseUrl('ftp://x.test', 'EVAL_API_BASE'), ConfigError);
    assert.throws(() => parseBaseUrl('https://u:p@x.test', 'EVAL_API_BASE'), /credentials/);
    assert.throws(() => parseBaseUrl('https://x.test/?key=1', 'EVAL_API_BASE'), /query/);
    assert.throws(() => parseBaseUrl('https://x.test/#a', 'EVAL_API_BASE'), /query or a fragment/);
    assert.throws(() => parseBaseUrl('not a url', 'EVAL_API_BASE'), /not a valid URL/);
  });
});

describe('TokenBudget and estimateTokens', () => {
  test('estimates one token per four characters and caps spending', () => {
    assert.equal(estimateTokens('abcd'), 1);
    assert.equal(estimateTokens('abcde'), 2);
    const budget = new TokenBudget(10);
    assert.equal(budget.exhausted, false);
    budget.record(10);
    assert.equal(budget.exhausted, true);
    assert.equal(new TokenBudget(null).exhausted, false);
    assert.equal(new TokenBudget(0).exhausted, true);
  });
});

describe('callEndpoint', () => {
  test('posts JSON and parses a JSON answer', async () => {
    const { fetchImpl, calls } = fakeFetch(() => ({ status: 200, body: { ok: true } }));
    const result = await callEndpoint({
      base: 'https://api.test', path: '/api/v1/x', body: { a: 1 }, timeoutMs: 1000, fetchImpl,
    });
    assert.deepEqual(result, { status: 200, body: { ok: true } });
    assert.equal(calls[0].url, 'https://api.test/api/v1/x');
    assert.equal(calls[0].init.method, 'POST');
    assert.equal(calls[0].init.body, '{"a":1}');
    assert.equal(calls[0].init.headers['content-type'], 'application/json');
  });

  test('keeps a non-JSON body as text and an empty body as null', async () => {
    const text = await callEndpoint({ base: 'https://a.test', path: '/p', timeoutMs: 1000,
      fetchImpl: fakeFetch(() => ({ status: 502, body: 'Bad Gateway' })).fetchImpl });
    assert.deepEqual(text, { status: 502, body: 'Bad Gateway' });
    const empty = await callEndpoint({ base: 'https://a.test', path: '/p', timeoutMs: 1000,
      fetchImpl: fakeFetch(() => ({ status: 200, body: '' })).fetchImpl });
    assert.deepEqual(empty, { status: 200, body: null });
  });

  test('turns a timeout into a TransportError without the URL in the message', async () => {
    const hang = (url, init) => new Promise((resolve, reject) => {
      init.signal.addEventListener('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })));
    });
    await assert.rejects(
      () => callEndpoint({ base: 'https://secret-host.test', path: '/p', timeoutMs: 20, fetchImpl: hang }),
      (error) => error.name === 'TransportError' && error.message === 'timeout after 20 ms' && !error.message.includes('secret-host'),
    );
  });

  test('turns a network failure into a generic TransportError', async () => {
    const boom = async () => { throw new TypeError('fetch failed for https://secret-host.test'); };
    await assert.rejects(
      () => callEndpoint({ base: 'https://secret-host.test', path: '/p', timeoutMs: 1000, fetchImpl: boom }),
      (error) => error.message === 'network error' && !error.message.includes('secret-host'),
    );
  });
});
