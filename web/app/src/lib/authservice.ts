import { authBaseUrl } from "@/lib/bff-config";

// Server-only client for authservice (konradcinkusz/authservice, the only identity provider). AUTHSERVICE-BRIEF §2.
// Passwords travel from the BFF to authservice over TLS and are never stored, logged or echoed back.
// Responses are parsed defensively: anything unexpected is treated as an upstream failure, never as a success.

const AUTH_TIMEOUT_MS = 10_000;
const DEFAULT_EXPIRES_IN_SECONDS = 3600;

export interface TokenPair {
  readonly accessToken: string;
  readonly refreshToken: string;
  readonly expiresIn: number;
}

/** Result of one call. "transient" = timeout or network error; any HTTP answer is returned with its status. */
export type AuthCall = { readonly kind: "http"; readonly status: number; readonly body: unknown } | { readonly kind: "transient" };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

async function call(
  path: string,
  method: "GET" | "POST",
  options: { readonly body?: unknown; readonly bearer?: string } = {},
): Promise<AuthCall | { readonly kind: "disabled" }> {
  const base = authBaseUrl();
  if (base === null) return { kind: "disabled" };
  const headers: Record<string, string> = { accept: "application/json" };
  if (options.body !== undefined) headers["content-type"] = "application/json";
  if (options.bearer !== undefined) headers.authorization = `Bearer ${options.bearer}`;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), AUTH_TIMEOUT_MS);
  try {
    const response = await fetch(`${base}${path}`, {
      method,
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      cache: "no-store",
      signal: controller.signal,
    });
    const text = await response.text();
    let body: unknown = null;
    if (text !== "") {
      try {
        body = JSON.parse(text);
      } catch {
        body = null;
      }
    }
    return { kind: "http", status: response.status, body };
  } catch {
    return { kind: "transient" };
  } finally {
    clearTimeout(timer);
  }
}

/** Parses a TokenResponse. Null when the required fields are missing. */
export function parseTokenPair(body: unknown): TokenPair | null {
  if (!isRecord(body)) return null;
  const { accessToken, refreshToken, expiresIn } = body;
  if (typeof accessToken !== "string" || accessToken === "") return null;
  if (typeof refreshToken !== "string" || refreshToken === "") return null;
  const seconds = typeof expiresIn === "number" && Number.isFinite(expiresIn) && expiresIn > 0 ? expiresIn : DEFAULT_EXPIRES_IN_SECONDS;
  return { accessToken, refreshToken, expiresIn: seconds };
}

export interface ConsentVersions {
  readonly terms: string;
  readonly privacy: string;
}

/** Current consent versions. Registration must send exactly these (AUTHSERVICE-BRIEF §2). Null when unavailable. */
export async function consentVersions(): Promise<ConsentVersions | null> {
  const outcome = await call("/api/v1/auth/consents/versions", "GET");
  if (outcome.kind !== "http" || outcome.status !== 200 || !isRecord(outcome.body)) return null;
  const { terms, privacy } = outcome.body;
  if (typeof terms !== "string" || typeof privacy !== "string" || terms === "" || privacy === "") return null;
  return { terms, privacy };
}

export function registerAccount(input: {
  readonly email: string;
  readonly password: string;
  readonly acceptedTermsVersion: string;
  readonly acceptedPrivacyVersion: string;
}) {
  return call("/api/v1/auth/register", "POST", {
    body: {
      email: input.email,
      password: input.password,
      acceptedTermsVersion: input.acceptedTermsVersion,
      acceptedPrivacyVersion: input.acceptedPrivacyVersion,
    },
  });
}

export function loginAccount(input: { readonly email: string; readonly password: string }) {
  return call("/api/v1/auth/login", "POST", { body: { email: input.email, password: input.password } });
}

/** Refresh rotates the refresh token: the caller must store the returned pair every time. */
export function refreshTokens(refreshToken: string) {
  return call("/api/v1/auth/refresh", "POST", { body: { refreshToken } });
}

export function logoutAccount(accessToken: string) {
  return call("/api/v1/auth/logout", "POST", { bearer: accessToken });
}

export function isDisabled(outcome: AuthCall | { readonly kind: "disabled" }): outcome is { readonly kind: "disabled" } {
  return outcome.kind === "disabled";
}
