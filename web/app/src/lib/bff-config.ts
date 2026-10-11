/**
 * Runtime configuration for the account and review BFF surface (SPEC-E, E3).
 * Every value is read per call from the environment, never inlined at build time (same rule as catalog-url.ts).
 * A missing value degrades the feature (P8): the routes answer 503 and the pages say the service is unavailable.
 */

export const SESSION_COOKIE = "ss_session";
/** Access tokens are refreshed when fewer than this many milliseconds remain. */
export const REFRESH_SKEW_MS = 60_000;
const MIN_SESSION_SECRET_BYTES = 32;

function normaliseBaseUrl(raw: string | undefined): string | null {
  if (raw === undefined) return null;
  const trimmed = raw.trim();
  if (trimmed === "") return null;
  try {
    const parsed = new URL(trimmed);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return null;
    return trimmed.replace(/\/+$/, "");
  } catch {
    return null;
  }
}

/** Base URL of authservice (AUTH_BASE_URL). Null when unset: account features are then unavailable. */
export function authBaseUrl(env: NodeJS.ProcessEnv = process.env): string | null {
  return normaliseBaseUrl(env.AUTH_BASE_URL);
}

/** Base URL of the Reviews service (REVIEWS_BASE_URL). Null when unset: review features are then unavailable. */
export function reviewsBaseUrl(env: NodeJS.ProcessEnv = process.env): string | null {
  return normaliseBaseUrl(env.REVIEWS_BASE_URL);
}

/** Base URL of the Billing service (BILLING_BASE_URL). Null when unset: billing features are then unavailable (P8). */
export function billingBaseUrl(env: NodeJS.ProcessEnv = process.env): string | null {
  return normaliseBaseUrl(env.BILLING_BASE_URL);
}

/** Shared secret that proves the BFF may vouch for X-Client-Ip (same contract as the catalog proxy). */
export function bffSharedSecret(env: NodeJS.ProcessEnv = process.env): string | null {
  const raw = env.BFF_SHARED_SECRET;
  return raw && raw.trim() !== "" ? raw : null;
}

/**
 * Raw key material for the sealed session cookie (SESSION_SECRET, at least 32 bytes).
 * Returns null when unset or too short: sessions cannot be sealed, so account features stay unavailable.
 */
export function sessionSecret(env: NodeJS.ProcessEnv = process.env): string | null {
  const raw = env.SESSION_SECRET;
  if (raw === undefined || Buffer.byteLength(raw, "utf8") < MIN_SESSION_SECRET_BYTES) return null;
  return raw;
}

/** True when every piece the account surface needs is configured. */
export function accountsConfigured(env: NodeJS.ProcessEnv = process.env): boolean {
  return authBaseUrl(env) !== null && sessionSecret(env) !== null;
}

/** Secure cookies everywhere except local development. */
export function secureCookies(env: NodeJS.ProcessEnv = process.env): boolean {
  return env.NODE_ENV !== "development";
}
