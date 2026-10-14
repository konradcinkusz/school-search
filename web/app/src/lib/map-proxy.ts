import { NextResponse, type NextRequest } from "next/server";
import { catalogCandidates } from "@/lib/catalog-url";
import type { MapMarker, MapMarkersResponse } from "@/lib/api";

/**
 * Server side of /api/map/markers (ADR-0022): forwards one validated query to the catalog's /api/v1/map/markers through
 * the candidate ladder (catalog-url.ts), re-emits only the contract fields and maps upstream failures to Polish
 * messages. The browser never sees a backend address, and nothing but the contract fields ever reaches the page.
 */
const UPSTREAM_TIMEOUT_MS = 15_000;

function errorResponse(status: number, code: string, message: string, extra?: Record<string, string>): NextResponse {
  return NextResponse.json({ error: { code, message } }, { status, headers: { "Cache-Control": "no-store", ...extra } });
}

function finite(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function moneyOrNull(value: unknown): number | null {
  return finite(value) ? value : null;
}

function cleanMarker(raw: unknown): MapMarker | null {
  if (typeof raw !== "object" || raw === null) return null;
  const r = raw as Record<string, unknown>;
  if (r.kind !== "school" && r.kind !== "preschool") return null;
  if (typeof r.slug !== "string" || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(r.slug) || r.slug.length > 120) return null;
  if (typeof r.name !== "string" || !finite(r.latitude) || !finite(r.longitude)) return null;
  if (r.latitude < -90 || r.latitude > 90 || r.longitude < -180 || r.longitude > 180) return null;
  const tuition =
    typeof r.monthlyTuition === "object" && r.monthlyTuition !== null
      ? (r.monthlyTuition as Record<string, unknown>)
      : null;
  return {
    kind: r.kind,
    slug: r.slug,
    name: r.name.slice(0, 200),
    latitude: r.latitude,
    longitude: r.longitude,
    isPublic: r.isPublic === true,
    gminaName: typeof r.gminaName === "string" ? r.gminaName.slice(0, 120) : null,
    monthlyTuition:
      r.kind === "preschool" && tuition !== null
        ? { minPln: moneyOrNull(tuition.minPln), maxPln: moneyOrNull(tuition.maxPln) }
        : null,
    freePlacesCount: r.kind === "preschool" && finite(r.freePlacesCount) ? r.freePlacesCount : null,
  };
}

/** Keeps only the contract fields. Returns null when the body is not a markers response. */
export function cleanMarkersResponse(body: unknown): MapMarkersResponse | null {
  if (typeof body !== "object" || body === null) return null;
  const b = body as Record<string, unknown>;
  if (!Array.isArray(b.items) || !finite(b.total) || typeof b.truncated !== "boolean" || !finite(b.limit)) return null;
  const items = b.items.map(cleanMarker).filter((item): item is MapMarker => item !== null);
  return { items, total: b.total, truncated: b.truncated, limit: b.limit };
}

function isAbortError(err: unknown): boolean {
  return err instanceof Error && (err.name === "AbortError" || err.name === "TimeoutError");
}

export async function forwardMarkers(request: NextRequest, upstream: URLSearchParams): Promise<NextResponse> {
  const clientIp = (request.headers.get("fly-client-ip") ?? request.headers.get("x-forwarded-for") ?? "")
    .split(",")[0]
    ?.trim();
  const headers: Record<string, string> = { accept: "application/json" };
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
      const response = await fetch(`${candidate.url}/api/v1/map/markers?${upstream.toString()}`, {
        method: "GET",
        headers,
        cache: "no-store",
        signal: controller.signal,
      });
      // 403 means this candidate is the wrong ingress; try the next rung of the ladder.
      if (response.status === 403) {
        clearTimeout(timer);
        lastFailure = `403 from ${candidate.source}`;
        await response.body?.cancel();
        continue;
      }
      const text = await response.text();
      clearTimeout(timer);

      if (response.status === 200) {
        let parsed: unknown = null;
        try {
          parsed = JSON.parse(text);
        } catch {
          parsed = null;
        }
        const clean = cleanMarkersResponse(parsed);
        if (clean === null) return errorResponse(502, "bad_upstream", "Katalog zwrócił nieoczekiwaną odpowiedź.");
        return NextResponse.json(clean, {
          headers: { "Cache-Control": "public, max-age=60", "X-Content-Type-Options": "nosniff" },
        });
      }
      if (response.status === 400) {
        return errorResponse(400, "validation_failed", "Nieprawidłowe parametry mapy. Zmień filtry lub obszar.");
      }
      if (response.status === 429) {
        const retryAfter = response.headers.get("retry-after");
        return errorResponse(
          429,
          "rate_limited",
          "Zbyt wiele zapytań. Spróbuj ponownie za chwilę.",
          retryAfter && /^[0-9]{1,5}$/.test(retryAfter) ? { "Retry-After": retryAfter } : undefined,
        );
      }
      if (response.status >= 500) {
        lastFailure = `${response.status} from ${candidate.source}`;
        continue;
      }
      return errorResponse(502, "bad_upstream", "Nie udało się pobrać placówek z katalogu.");
    } catch (err) {
      clearTimeout(timer);
      if (isAbortError(err)) {
        return errorResponse(504, "upstream_timeout", "Usługa katalogu nie odpowiedziała na czas.");
      }
      lastFailure = `network error from ${candidate.source}`;
    }
  }
  console.error(`[map] all catalog candidates failed: ${lastFailure}`);
  return errorResponse(503, "upstream_unavailable", "Usługa katalogu jest chwilowo niedostępna.");
}
