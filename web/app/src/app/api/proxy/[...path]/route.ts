import { NextResponse, type NextRequest } from "next/server";
import { catalogCandidates } from "@/lib/catalog-url";
import { isSlug, isToken } from "@/lib/validation";

// BFF catch-all (FRONTEND-BFF §5): the browser talks only to this origin; the backend address stays server-side.
// No cookie or Authorization header is forwarded: this surface is public and carries no session.
export const dynamic = "force-dynamic";

/** Covers a scale-to-zero cold start of the API machine. */
const UPSTREAM_TIMEOUT_MS = 60_000;
const MAX_BODY_BYTES = 16 * 1024;
const MAX_QUERY_LENGTH = 512;

type Route = "GET" | "POST";

/**
 * Explicit allowlist. Each entry is a fixed segment list, with a validator for any dynamic segment.
 * Anything not listed is a 404, including every admin path.
 */
const GET_ROUTES: ReadonlyArray<readonly string[] | ((segments: readonly string[]) => boolean)> = [
  ["api", "v1", "schools"],
  (s) => s.length === 4 && s[0] === "api" && s[1] === "v1" && s[2] === "schools" && isSlug(s[3] ?? ""),
  ["api", "v1", "ranking"],
  ["api", "v1", "compare"],
  ["api", "v1", "methodology"],
  ["api", "v1", "gminas"],
  ["api", "v1", "catchment"],
  ["api", "v1", "meta"],
  (s) =>
    s.length === 4 && s[0] === "api" && s[1] === "v1" && s[2] === "corrections" && isToken(s[3] ?? ""),
];

const POST_ROUTES: ReadonlyArray<readonly string[]> = [["api", "v1", "corrections"]];

const SAFE_SEGMENT = /^[A-Za-z0-9._~-]{1,200}$/;
const QUERY_PATTERN = /^[\x20-\x7E]*$/;

function matchesRoute(
  segments: readonly string[],
  routes: ReadonlyArray<readonly string[] | ((s: readonly string[]) => boolean)>,
): boolean {
  return routes.some((route) => {
    if (typeof route === "function") return route(segments);
    return route.length === segments.length && route.every((part, i) => segments[i] === part);
  });
}

function errorResponse(status: number, code: string, message: string): NextResponse {
  return NextResponse.json({ error: { code, message } }, { status, headers: { "Cache-Control": "no-store" } });
}

function isAbortError(err: unknown): boolean {
  return err instanceof Error && (err.name === "AbortError" || err.name === "TimeoutError");
}

function segmentsAreSafe(path: readonly string[]): boolean {
  return path.every((segment) => SAFE_SEGMENT.test(segment) && segment !== "." && segment !== "..");
}

/** Public data is cacheable at the edge for a short time; errors and corrections are not. */
function cacheControlFor(path: readonly string[], status: number): string {
  if (status !== 200) return "no-store";
  if (path[2] === "corrections") return "no-store";
  return "public, s-maxage=300, stale-while-revalidate=600";
}

/** Same-origin check for the one write route: a foreign Origin is refused. */
function originAllowed(request: NextRequest): boolean {
  const origin = request.headers.get("origin");
  if (origin === null) return true;
  const host = request.headers.get("host");
  try {
    return host !== null && new URL(origin).host === host;
  } catch {
    return false;
  }
}

async function forward(
  request: NextRequest,
  path: readonly string[],
  method: Route,
  body: string | null,
): Promise<NextResponse> {
  const suffix = path.map((segment) => encodeURIComponent(segment)).join("/");
  const search = request.nextUrl.search.length <= MAX_QUERY_LENGTH + 1 ? request.nextUrl.search : "";
  if (search.length > 0 && !QUERY_PATTERN.test(search)) {
    return errorResponse(400, "bad_query", "Nieprawidłowe parametry zapytania.");
  }

  // Forward the visitor address for rate limiting only. The catalog honours X-Client-Ip solely when the
  // shared secret matches, so no other caller can choose its own bucket.
  const clientIp = (request.headers.get("fly-client-ip") ?? request.headers.get("x-forwarded-for") ?? "")
    .split(",")[0]
    ?.trim();
  const headers: Record<string, string> = { accept: "application/json" };
  if (method === "POST") headers["content-type"] = "application/json";
  const bffSecret = process.env.BFF_SHARED_SECRET;
  if (bffSecret && clientIp && /^[0-9a-fA-F:.]{3,45}$/.test(clientIp)) {
    headers["x-client-ip"] = clientIp;
    headers["x-bff-secret"] = bffSecret;
  }

  let lastFailure = "no candidate reachable";
  for (const candidate of catalogCandidates()) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), UPSTREAM_TIMEOUT_MS);
    try {
      const upstream = await fetch(`${candidate.url}/${suffix}${search}`, {
        method,
        headers,
        body: method === "POST" ? body : undefined,
        cache: "no-store",
        signal: controller.signal,
      });

      // 403 means this candidate is the wrong ingress; try the next rung of the ladder.
      if (upstream.status === 403) {
        clearTimeout(timer);
        lastFailure = `403 from ${candidate.source}`;
        await upstream.body?.cancel();
        continue;
      }

      const text = await upstream.text();
      clearTimeout(timer);
      const responseHeaders = new Headers();
      const contentType = upstream.headers.get("content-type");
      if (contentType) responseHeaders.set("content-type", contentType);
      responseHeaders.set("cache-control", cacheControlFor(path, upstream.status));
      responseHeaders.set("x-content-type-options", "nosniff");
      return new NextResponse(text, { status: upstream.status, headers: responseHeaders });
    } catch (err) {
      clearTimeout(timer);
      if (isAbortError(err)) {
        return errorResponse(504, "upstream_timeout", "Usługa katalogu nie odpowiedziała na czas.");
      }
      lastFailure = `network error from ${candidate.source}`;
    }
  }

  // Detail stays in server logs only; the client never learns backend addresses.
  console.error(`[proxy] all catalog candidates failed: ${lastFailure}`);
  return errorResponse(503, "upstream_unavailable", "Usługa katalogu jest chwilowo niedostępna.");
}

export async function GET(
  request: NextRequest,
  context: { params: Promise<{ path: string[] }> },
): Promise<NextResponse> {
  const { path } = await context.params;
  if (!segmentsAreSafe(path) || !matchesRoute(path, GET_ROUTES)) {
    return errorResponse(404, "not_found", "Nie znaleziono zasobu.");
  }
  return forward(request, path, "GET", null);
}

export async function POST(
  request: NextRequest,
  context: { params: Promise<{ path: string[] }> },
): Promise<NextResponse> {
  const { path } = await context.params;
  if (!segmentsAreSafe(path) || !matchesRoute(path, POST_ROUTES)) {
    return errorResponse(404, "not_found", "Nie znaleziono zasobu.");
  }
  if (!originAllowed(request)) {
    return errorResponse(403, "forbidden_origin", "Żądanie z tej domeny nie jest dozwolone.");
  }
  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.toLowerCase().startsWith("application/json")) {
    return errorResponse(415, "unsupported_media_type", "Oczekiwano danych w formacie JSON.");
  }
  const declared = Number(request.headers.get("content-length") ?? "0");
  if (Number.isFinite(declared) && declared > MAX_BODY_BYTES) {
    return errorResponse(413, "payload_too_large", "Zgłoszenie jest za duże.");
  }
  const body = await request.text();
  if (new TextEncoder().encode(body).byteLength > MAX_BODY_BYTES) {
    return errorResponse(413, "payload_too_large", "Zgłoszenie jest za duże.");
  }
  return forward(request, path, "POST", body);
}

/** Every other method is refused: the proxy is read-only except for the corrections intake. */
export function PUT(): NextResponse {
  return errorResponse(405, "method_not_allowed", "Metoda nie jest dozwolona.");
}

export const PATCH = PUT;
export const DELETE = PUT;
