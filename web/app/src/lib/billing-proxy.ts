import { NextResponse, type NextRequest } from "next/server";
import { resolveSession, visitorIp, withRotatedCookie, type ServiceAccess, type SessionRotation } from "@/lib/bff-gate";
import { bffSharedSecret, billingBaseUrl } from "@/lib/bff-config";
import { billingUnavailable, bffError } from "@/lib/bff-http";
import { decodeClaims, hasModeratorRole } from "@/lib/jwt-claims";
import { isSlug } from "@/lib/validation";

// BFF forwarding for the Billing service (SPEC-F, F4). Exact-path allowlist with the same access model as the
// Reviews proxy. The payment provider webhook (/api/v1/billing/webhook) is deliberately NOT reachable from the
// browser: only the provider calls it, and it is verified by HMAC on the Billing side.

const BILLING_TIMEOUT_MS = 15_000;
const MAX_QUERY_LENGTH = 512;
const QUERY_PATTERN = /^[\x20-\x7E]*$/;
const UUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export interface BillingRoute {
  readonly method: "GET" | "POST" | "PUT" | "DELETE";
  readonly access: ServiceAccess;
}

const isId = (value: string | undefined): boolean => value !== undefined && UUID_PATTERN.test(value);
const isSchoolSlug = (value: string | undefined): boolean => value !== undefined && isSlug(value);

/** Exact allowlist of Billing paths. Anything else is not forwarded. */
export function billingRouteFor(path: readonly string[], method: string): BillingRoute | null {
  const [a, b, c, d, e, f] = path;
  if (a !== "api" || b !== "v1") return null;

  if (method === "GET") {
    if (path.length === 3 && c === "plans") return { method, access: "anonymous" };
    if (path.length === 4 && c === "legal" && d === "enquiry-consent") return { method, access: "anonymous" };
    if (path.length === 4 && c === "claims" && d === "mine") return { method, access: "required" };
    if (path.length === 5 && c === "schools" && isSchoolSlug(d) && e === "premium") {
      return { method, access: "anonymous" };
    }
    if (path.length === 5 && c === "schools" && isSchoolSlug(d) && e === "enquiries") {
      return { method, access: "required" };
    }
    if (path.length === 4 && c === "moderation" && d === "claims") return { method, access: "moderator" };
    return null;
  }

  if (method === "POST") {
    if (path.length === 5 && c === "schools" && isSchoolSlug(d) && e === "claims") return { method, access: "required" };
    if (path.length === 5 && c === "schools" && isSchoolSlug(d) && e === "enquiries") {
      return { method, access: "anonymous" };
    }
    if (path.length === 4 && c === "subscriptions" && d === "checkout") return { method, access: "required" };
    if (path.length === 5 && c === "subscriptions" && isId(d) && (e === "cancel" || e === "resume")) {
      return { method, access: "required" };
    }
    if (path.length === 4 && c === "billing" && d === "simulate") return { method, access: "required" };
    if (path.length === 4 && c === "premium" && isSchoolSlug(d) && e === "draft") return { method, access: "required" };
    if (path.length === 6 && c === "moderation" && d === "claims" && isId(e) && f === "decision") {
      return { method, access: "moderator" };
    }
    return null;
  }

  if (method === "PUT") {
    if (path.length === 5 && c === "schools" && isSchoolSlug(d) && e === "premium") return { method, access: "required" };
    return null;
  }

  if (method === "DELETE") {
    if (path.length === 4 && c === "enquiries" && isId(d)) return { method, access: "required" };
    return null;
  }
  return null;
}

/**
 * Forwards one allowed Billing call. `body` is the already validated JSON text or null. Billing answers are never
 * cached. Upstream timeouts become 504; an unreachable service becomes 503 billing_unavailable.
 */
export async function forwardToBilling(
  request: NextRequest,
  path: readonly string[],
  route: BillingRoute,
  body: string | null,
): Promise<NextResponse> {
  const base = billingBaseUrl();
  if (base === null) return billingUnavailable();

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

  const headers: Record<string, string> = { accept: "application/json" };
  if (body !== null) headers["content-type"] = "application/json";
  if (accessToken !== null) headers.authorization = `Bearer ${accessToken}`;
  const secret = bffSharedSecret();
  const clientIp = visitorIp(request);
  if (secret && clientIp) {
    headers["x-client-ip"] = clientIp;
    headers["x-bff-secret"] = secret;
  }

  const suffix = path.map((segment) => encodeURIComponent(segment)).join("/");
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), BILLING_TIMEOUT_MS);
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
    if (aborted) return bffError(504, "upstream_timeout", "Usługa płatności nie odpowiedziała na czas.");
    console.error(`[billing-proxy] upstream failed for ${route.method} /${path.slice(0, 2).join("/")}/...`);
    return billingUnavailable();
  } finally {
    clearTimeout(timer);
  }
}
