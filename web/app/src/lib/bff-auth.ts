import type { NextRequest, NextResponse } from "next/server";
import { authBaseUrl, REFRESH_SKEW_MS, SESSION_COOKIE, secureCookies, sessionSecret } from "@/lib/bff-config";
import { parseTokenPair, refreshTokens, type TokenPair, isDisabled } from "@/lib/authservice";
import { decodeClaims, emailClaim } from "@/lib/jwt-claims";
import {
  deriveSessionKey,
  maskEmail,
  newSessionId,
  sealSession,
  tokenFingerprint,
  unsealSession,
  type SessionPayload,
} from "@/lib/session";

// Server-only session orchestration for the BFF. Route handlers and the reviews proxy use these helpers; the browser
// only ever holds the sealed cookie.

const RECENT_ROTATION_TTL_MS = 60_000;
const RECENT_ROTATION_LIMIT = 5_000;

let cachedKeySource: string | null = null;
let cachedKey: Buffer | null = null;

/** Derived cookie key, or null when SESSION_SECRET is missing or too short. */
export function sessionKey(): Buffer | null {
  const secret = sessionSecret();
  if (secret === null) return null;
  if (cachedKey === null || cachedKeySource !== secret) {
    cachedKey = deriveSessionKey(secret);
    cachedKeySource = secret;
  }
  return cachedKey;
}

export function readSession(request: NextRequest): SessionPayload | null {
  const key = sessionKey();
  if (key === null) return null;
  return unsealSession(request.cookies.get(SESSION_COOKIE)?.value, key);
}

/** Builds the session state from a fresh token pair. The email is taken from the access token claim. */
export function sessionFromTokens(pair: TokenPair, sid: string, previousEmailMasked?: string): SessionPayload {
  const email = emailClaim(decodeClaims(pair.accessToken));
  return {
    sid,
    accessToken: pair.accessToken,
    refreshToken: pair.refreshToken,
    expiresAt: Date.now() + pair.expiresIn * 1000,
    userEmailMasked: email ? maskEmail(email) : (previousEmailMasked ?? "***"),
  };
}

export function newSessionFromTokens(pair: TokenPair): SessionPayload {
  return sessionFromTokens(pair, newSessionId());
}

function cookieOptions(maxAgeSeconds: number) {
  return {
    name: SESSION_COOKIE,
    httpOnly: true,
    secure: secureCookies(),
    sameSite: "lax" as const,
    path: "/",
    maxAge: maxAgeSeconds,
  };
}

const THIRTY_DAYS_SECONDS = 30 * 24 * 60 * 60;

/** Writes the sealed cookie onto a response. Returns false (and writes nothing) when sealing is not configured. */
export function setSessionCookie(response: NextResponse, payload: SessionPayload): boolean {
  const key = sessionKey();
  if (key === null) return false;
  response.cookies.set({ ...cookieOptions(THIRTY_DAYS_SECONDS), value: sealSession(payload, key) });
  return true;
}

export function clearSessionCookie(response: NextResponse): void {
  response.cookies.set({ ...cookieOptions(0), value: "" });
}

export type FreshSession =
  | { readonly kind: "fresh"; readonly session: SessionPayload; readonly rotated: boolean }
  | { readonly kind: "expired" }
  | { readonly kind: "unavailable" }
  | { readonly kind: "disabled" };

type RefreshResult = { readonly kind: "ok"; readonly session: SessionPayload } | { readonly kind: "expired" } | { readonly kind: "unavailable" } | { readonly kind: "disabled" };

/**
 * Single-flight refresh per session id: concurrent requests carrying the same session share one refresh call.
 * Refresh tokens rotate and a reused one revokes the whole family (AUTHSERVICE-BRIEF G7), so parallel refreshes
 * must never race. Limitation: this map is per process; a multi-instance deployment needs sticky routing or a
 * shared lock. A rotation result is also kept for a short time so a request that still carries the previous cookie
 * gets the rotated tokens instead of replaying the revoked refresh token.
 */
const inflight = new Map<string, Promise<RefreshResult>>();
const recentRotations = new Map<string, { readonly at: number; readonly session: SessionPayload }>();

function rememberRotation(previousRefresh: string, session: SessionPayload): void {
  const now = Date.now();
  for (const [key, entry] of recentRotations) {
    if (now - entry.at > RECENT_ROTATION_TTL_MS) recentRotations.delete(key);
  }
  if (recentRotations.size >= RECENT_ROTATION_LIMIT) {
    const oldest = recentRotations.keys().next();
    if (!oldest.done) recentRotations.delete(oldest.value);
  }
  recentRotations.set(tokenFingerprint(previousRefresh), { at: now, session });
}

async function performRefresh(current: SessionPayload): Promise<RefreshResult> {
  const recent = recentRotations.get(tokenFingerprint(current.refreshToken));
  if (recent && Date.now() - recent.at <= RECENT_ROTATION_TTL_MS) {
    return { kind: "ok", session: recent.session };
  }
  const outcome = await refreshTokens(current.refreshToken);
  if (isDisabled(outcome)) return { kind: "disabled" };
  if (outcome.kind === "transient") return { kind: "unavailable" };
  if (outcome.status === 401 || outcome.status === 403) return { kind: "expired" };
  if (outcome.status !== 200) return { kind: "unavailable" };
  const pair = parseTokenPair(outcome.body);
  if (pair === null) return { kind: "unavailable" };
  const session = sessionFromTokens(pair, current.sid, current.userEmailMasked);
  rememberRotation(current.refreshToken, session);
  return { kind: "ok", session };
}

/** Returns a session whose access token is valid for at least REFRESH_SKEW_MS, refreshing when needed. */
export async function ensureFresh(current: SessionPayload): Promise<FreshSession> {
  if (current.expiresAt - Date.now() > REFRESH_SKEW_MS) {
    return { kind: "fresh", session: current, rotated: false };
  }
  if (authBaseUrl() === null) return { kind: "disabled" };

  let pending = inflight.get(current.sid);
  if (pending === undefined) {
    pending = performRefresh(current).finally(() => inflight.delete(current.sid));
    inflight.set(current.sid, pending);
  }
  const result = await pending;
  if (result.kind === "ok") return { kind: "fresh", session: result.session, rotated: true };
  return result;
}

/** Same-origin check for state-changing BFF calls. A request without an Origin header (non-browser) is refused. */
export function isSameOrigin(request: NextRequest): boolean {
  const origin = request.headers.get("origin");
  const host = request.headers.get("host");
  if (origin === null || host === null) return false;
  try {
    return new URL(origin).host === host;
  } catch {
    return false;
  }
}
