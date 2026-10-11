import { NextResponse, type NextRequest } from "next/server";
import { clearSessionCookie, ensureFresh, readSession, setSessionCookie } from "@/lib/bff-auth";
import { accountsConfigured, bffSharedSecret, reviewsBaseUrl } from "@/lib/bff-config";
import { accountsUnavailable, bffError, reviewsUnavailable } from "@/lib/bff-http";
import { decodeClaims, hasModeratorRole } from "@/lib/jwt-claims";
import type { SessionPayload } from "@/lib/session";
import { isSlug } from "@/lib/validation";

// BFF forwarding for the Reviews service (SPEC-E §E3). Exact-path allowlist, server-side Bearer injection and the
// visitor address for rate limits. The browser never sends a token; the Reviews service validates every token itself.

const REVIEWS_TIMEOUT_MS = 15_000;
const MAX_QUERY_LENGTH = 512;
const QUERY_PATTERN = /^[\x20-\x7E]*$/;
const UUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const MODERATION_KINDS: ReadonlySet<string> = new Set(["reviews", "replies", "notices", "complaints"]);

/**
 * anonymous: no session needed.
 * optional: a session is attached when present (complaints may also be filed with a notice token instead).
 * required: a session is needed; the service itself enforces email verification and ownership.
 * moderator: a session plus a moderator role claim. Gating only: the service re-checks every call.
 */
export type ReviewsAccess = "anonymous" | "optional" | "required" | "moderator";

export interface ReviewsRoute {
  readonly method: "GET" | "POST" | "DELETE";
  readonly access: ReviewsAccess;
}

const isId = (value: string | undefined): boolean => value !== undefined && UUID_PATTERN.test(value);

/** Exact allowlist of Reviews paths. Anything else is not forwarded. */
export function reviewsRouteFor(path: readonly string[], method: string): ReviewsRoute | null {
  const [a, b, c, d, e, f] = path;
  if (a !== "api" || b !== "v1") return null;

  if (method === "GET") {
    if (path.length === 5 && c === "schools" && isSlug(d ?? "") && e === "reviews") return { method, access: "anonymous" };
    if (path.length === 4 && c === "reviews" && d === "mine") return { method, access: "required" };
    if (path.length === 4 && c === "notices" && isId(d)) return { method, access: "anonymous" };
    if (path.length === 4 && c === "legal" && (d === "terms" || d === "contact" || d === "transparency")) {
      return { method, access: "anonymous" };
    }
    if (path.length === 4 && c === "moderation" && d === "queue") return { method, access: "moderator" };
    return null;
  }

  if (method === "POST") {
    if (path.length === 5 && c === "schools" && isSlug(d ?? "") && e === "reviews") return { method, access: "required" };
    if (path.length === 5 && c === "reviews" && isId(d) && e === "reply") return { method, access: "required" };
    if (path.length === 3 && c === "notices") return { method, access: "anonymous" };
    if (path.length === 5 && c === "decisions" && isId(d) && e === "complaints") return { method, access: "optional" };
    if (path.length === 6 && c === "moderation" && MODERATION_KINDS.has(d ?? "") && isId(e) && f === "decision") {
      return { method, access: "moderator" };
    }
    return null;
  }

  if (method === "DELETE") {
    if (path.length === 4 && c === "reviews" && isId(d)) return { method, access: "required" };
    return null;
  }
  return null;
}

function segmentsAreSafe(path: readonly string[]): boolean {
  return path.every((segment) => /^[A-Za-z0-9._~-]{1,200}$/.test(segment) && segment !== "." && segment !== "..");
}

export function reviewsPathAllowed(path: readonly string[], method: string): boolean {
  return segmentsAreSafe(path) && reviewsRouteFor(path, method) !== null;
}

/** Session failures for a required or moderator route. Returns a response to send, or the session to use. */
type Resolved =
  | { readonly kind: "anonymous" }
  | { readonly kind: "session"; readonly accessToken: string; readonly rotated: SessionRotation }
  | { readonly kind: "respond"; readonly response: NextResponse };

type SessionRotation = { readonly payload: SessionPayload } | null;

async function resolveSession(request: NextRequest, access: ReviewsAccess): Promise<Resolved> {
  const session = readSession(request);
  if (access === "anonymous") return { kind: "anonymous" };

  if (session === null) {
    if (access === "optional") return { kind: "anonymous" };
    return {
      kind: "respond",
      response: bffError(401, "unauthenticated", "Zaloguj się, aby kontynuować."),
    };
  }

  if (!accountsConfigured()) {
    if (access === "optional") return { kind: "anonymous" };
    return { kind: "respond", response: accountsUnavailable() };
  }

  const fresh = await ensureFresh(session);
  if (fresh.kind === "fresh") {
    return {
      kind: "session",
      accessToken: fresh.session.accessToken,
      rotated: fresh.rotated ? { payload: fresh.session } : null,
    };
  }
  if (access === "optional") return { kind: "anonymous" };
  if (fresh.kind === "expired") {
    const response = bffError(401, "unauthenticated", "Sesja wygasła. Zaloguj się ponownie.");
    clearSessionCookie(response);
    return { kind: "respond", response };
  }
  return { kind: "respond", response: accountsUnavailable() };
}

function withRotatedCookie(response: NextResponse, rotated: SessionRotation): NextResponse {
  if (rotated) setSessionCookie(response, rotated.payload);
  return response;
}

/**
 * Forwards one allowed Reviews call. `body` is the already validated JSON text or null.
 * Upstream timeouts become 504, unreachable service becomes 503 reviews_unavailable. Nothing about backend addresses leaks.
 */
export async function forwardToReviews(
  request: NextRequest,
  path: readonly string[],
  route: ReviewsRoute,
  body: string | null,
): Promise<NextResponse> {
  const base = reviewsBaseUrl();
  if (base === null) return reviewsUnavailable();

  const resolved = await resolveSession(request, route.access);
  if (resolved.kind === "respond") return resolved.response;

  let accessToken: string | null = null;
  let rotated: SessionRotation = null;
  if (resolved.kind === "session") {
    accessToken = resolved.accessToken;
    rotated = resolved.rotated;
    if (route.access === "moderator" && !hasModeratorRole(decodeClaims(accessToken))) {
      return withRotatedCookie(
        bffError(403, "forbidden_role", "Ta strona jest dostępna wyłącznie dla moderatorów."),
        rotated,
      );
    }
  } else if (route.access === "moderator") {
    return bffError(401, "unauthenticated", "Zaloguj się, aby kontynuować.");
  }

  const search = request.nextUrl.search.length <= MAX_QUERY_LENGTH + 1 ? request.nextUrl.search : "";
  if (search.length > 0 && !QUERY_PATTERN.test(search)) {
    return bffError(400, "bad_query", "Nieprawidłowe parametry zapytania.");
  }

  const clientIp = (request.headers.get("fly-client-ip") ?? request.headers.get("x-forwarded-for") ?? "")
    .split(",")[0]
    ?.trim();
  const headers: Record<string, string> = { accept: "application/json" };
  if (body !== null) headers["content-type"] = "application/json";
  if (accessToken !== null) headers.authorization = `Bearer ${accessToken}`;
  const secret = bffSharedSecret();
  if (secret && clientIp && /^[0-9a-fA-F:.]{3,45}$/.test(clientIp)) {
    headers["x-client-ip"] = clientIp;
    headers["x-bff-secret"] = secret;
  }

  const suffix = path.map((segment) => encodeURIComponent(segment)).join("/");
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REVIEWS_TIMEOUT_MS);
  try {
    const upstream = await fetch(`${base}/${suffix}${search}`, {
      method: route.method,
      headers,
      body: body ?? undefined,
      cache: "no-store",
      signal: controller.signal,
    });
    const text = await upstream.text();
    const responseHeaders = new Headers({
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
    });
    const contentType = upstream.headers.get("content-type");
    if (contentType) responseHeaders.set("content-type", contentType);
    const nullBody = upstream.status === 204 || upstream.status === 304;
    const response = new NextResponse(nullBody ? null : text, { status: upstream.status, headers: responseHeaders });
    return withRotatedCookie(response, rotated);
  } catch (err) {
    const aborted = err instanceof Error && (err.name === "AbortError" || err.name === "TimeoutError");
    if (aborted) return bffError(504, "upstream_timeout", "Usługa opinii nie odpowiedziała na czas.");
    console.error(`[reviews-proxy] upstream failed for ${route.method} /${path.slice(0, 2).join("/")}/...`);
    return reviewsUnavailable();
  } finally {
    clearTimeout(timer);
  }
}

