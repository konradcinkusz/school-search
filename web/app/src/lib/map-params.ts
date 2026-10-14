/**
 * Validation of the browser's marker query before it is forwarded to the catalog (/api/map/markers, ADR-0022).
 * Only the parameters of the contract are forwarded, each re-validated and clamped here, so the browser can never
 * pass an arbitrary query string to the API. Pure and dependency-free.
 */
export const MARKER_LIMIT_DEFAULT = 300;
export const MARKER_LIMIT_MAX = 500;
export const MARKER_TUITION_MAX = 20_000;
const Q_MIN = 2;
const Q_MAX = 100;

export type MarkerQueryResult =
  | { readonly ok: true; readonly upstream: URLSearchParams }
  | { readonly ok: false; readonly message: string };

const BAD_AREA = "Nieprawidłowy obszar mapy.";

function decimal(raw: string | null): number | null {
  if (raw === null || !/^-?[0-9]{1,3}(?:\.[0-9]{1,12})?$/.test(raw.trim())) return null;
  const value = Number.parseFloat(raw);
  return Number.isFinite(value) ? value : null;
}

function round6(value: number): string {
  return String(Math.round(value * 1e6) / 1e6);
}

/** Accepts "true" or "false" (also absent). Returns undefined for anything else. */
function bool(raw: string | null): boolean | null | undefined {
  if (raw === null || raw === "") return null;
  if (raw === "true") return true;
  if (raw === "false") return false;
  return undefined;
}

export function parseMarkerQuery(params: URLSearchParams): MarkerQueryResult {
  const south = decimal(params.get("south"));
  const west = decimal(params.get("west"));
  const north = decimal(params.get("north"));
  const east = decimal(params.get("east"));
  if (south === null || west === null || north === null || east === null) return { ok: false, message: BAD_AREA };
  if (south < -90 || north > 90 || west < -180 || east > 180 || south >= north || west >= east) {
    return { ok: false, message: BAD_AREA };
  }

  const upstream = new URLSearchParams();
  upstream.set("south", round6(south));
  upstream.set("west", round6(west));
  upstream.set("north", round6(north));
  upstream.set("east", round6(east));

  const kind = params.get("kind");
  if (kind !== null && kind !== "") {
    if (kind !== "school" && kind !== "preschool" && kind !== "all") {
      return { ok: false, message: "Nieprawidłowy rodzaj placówki." };
    }
    if (kind !== "all") upstream.set("kind", kind);
  }

  const q = params.get("q")?.replace(/\s+/g, " ").trim();
  // Fewer than two characters is not a usable name filter; it is dropped (the page tells the visitor).
  if (q !== undefined && q.length >= Q_MIN) upstream.set("q", q.slice(0, Q_MAX));

  const gmina = params.get("gmina");
  if (gmina !== null && gmina !== "") {
    if (!/^[0-9]{7}$/.test(gmina)) return { ok: false, message: "Nieprawidłowy kod gminy." };
    upstream.set("gmina", gmina);
  }

  const publicOnly = bool(params.get("publicOnly"));
  if (publicOnly === undefined) return { ok: false, message: "Nieprawidłowy filtr placówek publicznych." };
  if (publicOnly === true) upstream.set("publicOnly", "true");

  const hasFreePlaces = bool(params.get("hasFreePlaces"));
  if (hasFreePlaces === undefined) return { ok: false, message: "Nieprawidłowy filtr wolnych miejsc." };
  if (hasFreePlaces === true) upstream.set("hasFreePlaces", "true");

  const tuition = params.get("maxTuition");
  if (tuition !== null && tuition !== "") {
    if (!/^[0-9]{1,5}$/.test(tuition) || Number(tuition) > MARKER_TUITION_MAX) {
      return { ok: false, message: "Maksymalne czesne podaj w pełnych złotych, od 0 do 20 000." };
    }
    upstream.set("maxTuition", String(Number(tuition)));
  }

  const limitRaw = params.get("limit");
  let limit = MARKER_LIMIT_DEFAULT;
  if (limitRaw !== null && limitRaw !== "") {
    if (!/^[0-9]{1,6}$/.test(limitRaw)) return { ok: false, message: "Nieprawidłowy limit wyników." };
    limit = Math.min(Math.max(Number(limitRaw), 1), MARKER_LIMIT_MAX);
  }
  upstream.set("limit", String(limit));

  return { ok: true, upstream };
}
