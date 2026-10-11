import { NextResponse, type NextRequest } from "next/server";
import { resolveSession, withRotatedCookie, type SessionRotation } from "@/lib/bff-gate";
import { bffError } from "@/lib/bff-http";
import { catalogCandidates } from "@/lib/catalog-url";
import { decodeClaims, hasModeratorRole } from "@/lib/jwt-claims";

// BFF forwarding for the operator corrections surface of the catalog API (SPEC-G, feature 5). The catalog's admin
// routes need an authservice bearer token, so the token is injected here from the sealed session, never taken from
// the browser. The catalog validates the token and the Admin/SuperAdmin role again on every call.

const ADMIN_TIMEOUT_MS = 20_000;
const MAX_QUERY_LENGTH = 512;
const QUERY_PATTERN = /^[\x20-\x7E]*$/;
const UUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export interface CatalogAdminRoute {
  readonly method: "GET" | "POST";
  readonly access: "moderator";
}

/** Exact allowlist: the corrections list and the AI triage of one correction. Nothing else under /admin. */
export function catalogAdminRouteFor(path: readonly string[], method: string): CatalogAdminRoute | null {
  const [a, b, c, d, e, f] = path;
  if (a !== "api" || b !== "v1" || c !== "admin" || d !== "corrections") return null;
  if (method === "GET" && path.length === 4) return { method, access: "moderator" };
  if (method === "POST" && path.length === 6 && e !== undefined && UUID_PATTERN.test(e) && f === "triage") {
    return { method, access: "moderator" };
  }
  return null;
}

/** Forwards one allowed admin call. The body is always null: triage takes no input from the browser. */
export async function forwardToCatalogAdmin(
  request: NextRequest,
  path: readonly string[],
  route: CatalogAdminRoute,
): Promise<NextResponse> {
  const resolved = await resolveSession(request, "moderator");
  if (resolved.kind === "respond") return resolved.response;
  if (resolved.kind !== "session") {
    return bffError(401, "unauthenticated", "Zaloguj się, aby kontynuować.");
  }
  const rotated: SessionRotation = resolved.rotated;
  if (!hasModeratorRole(decodeClaims(resolved.accessToken))) {
    return withRotatedCookie(bffError(403, "forbidden_role", "Ta strona jest dostępna wyłącznie dla moderatorów."), rotated);
  }

  const search = request.nextUrl.search.length <= MAX_QUERY_LENGTH + 1 ? request.nextUrl.search : "";
  if (search.length > 0 && !QUERY_PATTERN.test(search)) {
    return bffError(400, "bad_query", "Nieprawidłowe parametry zapytania.");
  }

  const headers: Record<string, string> = {
    accept: "application/json",
    authorization: `Bearer ${resolved.accessToken}`,
  };
  const suffix = path.map((segment) => encodeURIComponent(segment)).join("/");

  // Walk the candidate ladder on network failure only. Any HTTP answer (including a role refusal) is returned as is.
  for (const candidate of catalogCandidates()) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), ADMIN_TIMEOUT_MS);
    try {
      const upstream = await fetch(`${candidate.url}/${suffix}${search}`, {
        method: route.method,
        headers,
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
      return withRotatedCookie(new NextResponse(text, { status: upstream.status, headers: responseHeaders }), rotated);
    } catch (err) {
      const aborted = err instanceof Error && (err.name === "AbortError" || err.name === "TimeoutError");
      if (aborted) return withRotatedCookie(bffError(504, "upstream_timeout", "Usługa nie odpowiedziała na czas."), rotated);
      console.error(`[admin-proxy] candidate ${candidate.source} unreachable`);
    } finally {
      clearTimeout(timer);
    }
  }
  return withRotatedCookie(bffError(503, "upstream_unavailable", "Usługa katalogu jest chwilowo niedostępna."), rotated);
}
