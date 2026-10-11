import { ConfigError } from './errors.mjs';

// Error codes the product answers with when the model did not run (src/SchoolSearch.Api/Ai, src/SchoolSearch.Mcp/Agent).
export const AI_NOT_AVAILABLE_CODES = Object.freeze(['ai_unavailable', 'ai_budget_exhausted']);

export const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export class TransportError extends Error {
  constructor(reason) {
    super(reason);
    this.name = 'TransportError';
  }
}

// Validates a base URL from the environment. Returns it without a trailing slash, or null when unset.
// Only http and https; no credentials, query or fragment (they would end up in logs and reports).
export function parseBaseUrl(raw, name) {
  if (raw === undefined || raw === null || String(raw).trim() === '') return null;
  let url;
  try {
    url = new URL(String(raw).trim());
  } catch {
    throw new ConfigError(`${name} is not a valid URL`);
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new ConfigError(`${name} must start with http:// or https://`);
  }
  if (url.username !== '' || url.password !== '') {
    throw new ConfigError(`${name} must not contain credentials`);
  }
  if (url.search !== '' || url.hash !== '') {
    throw new ConfigError(`${name} must not contain a query or a fragment`);
  }
  return url.href.replace(/\/+$/, '');
}

// Estimated tokens: the product responses do not report usage, so the harness counts about one token per four
// characters of request and response JSON. The report labels the figure as an estimate.
export function estimateTokens(text) {
  return Math.ceil(String(text).length / 4);
}

// Per-run cap on estimated tokens. limit null means no cap.
export class TokenBudget {
  constructor(limit) {
    this.limit = limit;
    this.spent = 0;
  }

  get exhausted() {
    return this.limit !== null && this.spent >= this.limit;
  }

  record(tokens) {
    this.spent += tokens;
  }
}

// One HTTP call, one attempt, no retry. Resolves to { status, body }. Rejects with TransportError, whose message
// never contains the URL, the request or the response.
export async function callEndpoint({ base, path, method = 'POST', body, headers = {}, timeoutMs, fetchImpl }) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  const init = {
    method,
    headers: {
      accept: 'application/json',
      ...(body === undefined ? {} : { 'content-type': 'application/json' }),
      ...headers,
    },
    signal: controller.signal,
  };
  if (body !== undefined) init.body = JSON.stringify(body);
  try {
    const response = await fetchImpl(base + path, init);
    const text = await response.text();
    let parsed;
    if (text === '') {
      parsed = null;
    } else {
      try {
        parsed = JSON.parse(text);
      } catch {
        parsed = text;
      }
    }
    return { status: response.status, body: parsed };
  } catch (error) {
    if (error && error.name === 'AbortError') {
      throw new TransportError(`timeout after ${timeoutMs} ms`);
    }
    throw new TransportError('network error');
  } finally {
    clearTimeout(timer);
  }
}
