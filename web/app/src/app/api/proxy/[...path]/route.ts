import { NextResponse, type NextRequest } from "next/server";
import { catalogCandidates } from "@/lib/catalog-url";

// BFF catch-all: the browser only ever talks to this origin; the backend address stays server-side.
export const dynamic = "force-dynamic";

/** Covers a scale-to-zero cold start of the API machine (FRONTEND-BFF §5). */
const UPSTREAM_TIMEOUT_MS = 60_000;

/** Only the public, read-only catalog surface is proxied in Phase A. */
const ALLOWED_PREFIX = ["api", "v1"] as const;

const PASSTHROUGH_HEADERS = ["content-type", "cache-control"] as const;

function errorResponse(status: number, code: string, message: string): NextResponse {
  return NextResponse.json({ error: { code, message } }, { status });
}

function isAbortError(err: unknown): boolean {
  return err instanceof Error && (err.name === "AbortError" || err.name === "TimeoutError");
}

export async function GET(
  request: NextRequest,
  context: { params: Promise<{ path: string[] }> },
): Promise<NextResponse> {
  const { path } = await context.params;

  const allowed = ALLOWED_PREFIX.every((segment, i) => path[i] === segment);
  const safe = path.every((segment) => segment !== "" && segment !== "." && segment !== "..");
  if (!allowed || !safe) {
    return errorResponse(404, "not_found", "Nie znaleziono zasobu.");
  }

  const suffix = path.map((segment) => encodeURIComponent(segment)).join("/");
  const search = request.nextUrl.search;

  let lastFailure = "no candidate reachable";
  for (const candidate of catalogCandidates()) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), UPSTREAM_TIMEOUT_MS);
    try {
      const upstream = await fetch(`${candidate.url}/${suffix}${search}`, {
        method: "GET",
        headers: { accept: "application/json" },
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

      const headers = new Headers();
      for (const name of PASSTHROUGH_HEADERS) {
        const value = upstream.headers.get(name);
        if (value) headers.set(name, value);
      }
      // Body is streamed; the timer stays armed so a stalled body is bounded by the same budget.
      return new NextResponse(upstream.body, { status: upstream.status, headers });
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
