import type { MapMarker, MapMarkersResponse } from "@/lib/api";
import { formatInteger } from "@/lib/format";
import { freePlacesValue, tuitionLabel } from "@/lib/preschool";
import { filtersToSearchParams, type SearchFilters } from "@/lib/search-filters";

/**
 * Pure helpers for the map page. Markers are neutral: one colour and one shape for every school, another colour and
 * another shape for every preschool. Nothing here reads, sorts or sizes by a score, a rank or a review (AGENTS.md).
 */

/** Marker colours (WCAG 1.4.11: at least 3:1 against white, and a white outline keeps them readable on any tile). */
export const MARKER_COLOR = {
  /** Petrol, the brand ink: 7.2:1 on white. */
  school: "#0b5d6b",
  /** Burnt orange: 5.6:1 on white. Distinct from petrol for common colour vision deficiencies by shape and by hue. */
  preschool: "#b3470f",
  /** Neutral cluster ink (no meaning attached). */
  cluster: "#15202b",
} as const;

export const KIND_LABEL: Record<MapMarker["kind"], string> = { school: "Szkoła", preschool: "Przedszkole" };

/** Continental Poland, with a margin. The list fallback asks for this whole area. */
export const POLAND_BOUNDS = { south: 48.9, west: 13.9, north: 55.0, east: 24.3 } as const;
export const POLAND_CENTER = { lat: 52.0, lng: 19.1 } as const;
export const DEFAULT_ZOOM = 6;

export interface Bounds {
  readonly south: number;
  readonly west: number;
  readonly north: number;
  readonly east: number;
}

/** Clamps a viewport to the ranges the API accepts. A viewport wrapping the antimeridian becomes the whole longitude range. */
export function normaliseBounds(b: Bounds): Bounds | null {
  const south = Math.max(-90, Math.min(90, b.south));
  const north = Math.max(-90, Math.min(90, b.north));
  let west = b.west;
  let east = b.east;
  if (west > east || east - west >= 360) {
    west = -180;
    east = 180;
  }
  west = Math.max(-180, west);
  east = Math.min(180, east);
  if (!(south < north) || !(west < east)) return null;
  return { south, west, north, east };
}

/** Bounds of a circle (radius in km) around a point, for fitting the map after a place was found. */
export function boundsAround(lat: number, lng: number, radiusKm: number): Bounds {
  const dLat = radiusKm / 111.32;
  const dLng = radiusKm / (111.32 * Math.max(0.1, Math.cos((lat * Math.PI) / 180)));
  return { south: lat - dLat, north: lat + dLat, west: lng - dLng, east: lng + dLng };
}

/** The query sent to /api/map/markers: bounds plus the filters the contract knows. Preschool-only filters are not sent for schools. */
export function markerQuery(bounds: Bounds, filters: SearchFilters, limit?: number): URLSearchParams {
  const params = new URLSearchParams();
  params.set("south", bounds.south.toFixed(6));
  params.set("west", bounds.west.toFixed(6));
  params.set("north", bounds.north.toFixed(6));
  params.set("east", bounds.east.toFixed(6));
  const shared = filtersToSearchParams({
    kind: filters.kind,
    q: filters.q !== undefined && filters.q.length >= 2 ? filters.q : undefined,
    gmina: filters.gmina,
    publicOnly: filters.publicOnly,
    maxTuitionPln: filters.kind === "school" ? undefined : filters.maxTuitionPln,
    hasFreePlaces: filters.kind === "school" ? undefined : filters.hasFreePlaces,
  });
  for (const [key, value] of shared) params.set(key, value);
  if (limit !== undefined) params.set("limit", String(limit));
  return params;
}

/** Name sort for the list: alphabetical in Polish, never by quality. */
export function sortByName(items: readonly MapMarker[]): MapMarker[] {
  return [...items].sort((a, b) => a.name.localeCompare(b.name, "pl") || a.slug.localeCompare(b.slug));
}

export function profileHref(marker: Pick<MapMarker, "kind" | "slug">): string {
  return marker.kind === "school" ? `/szkoly/${marker.slug}` : `/przedszkola/${marker.slug}`;
}

export function publicLabel(marker: Pick<MapMarker, "kind" | "isPublic">): string {
  if (marker.kind === "preschool") return marker.isPublic ? "przedszkole publiczne" : "przedszkole niepubliczne";
  return marker.isPublic ? "szkoła publiczna" : "szkoła niepubliczna";
}

/** Preschool facts as text. A missing value reads "brak danych" (never zero, never a guess). */
export function preschoolFacts(marker: MapMarker): { tuition: string; freePlaces: string } {
  const tuition = marker.monthlyTuition ? tuitionLabel(marker.monthlyTuition) : null;
  const places = freePlacesValue(marker.freePlacesCount);
  return { tuition: tuition ?? "brak danych", freePlaces: places ?? "brak danych" };
}

/** aria-live text for the result count. */
export function countMessage(response: MapMarkersResponse): string {
  const shown = response.items.length;
  if (response.total === 0) return "Brak placówek w tym obszarze dla wybranych filtrów.";
  if (response.truncated) {
    return `Placówek w tym obszarze: ${formatInteger(response.total)}. Pokazano ${formatInteger(shown)}.`;
  }
  return `Placówek w tym obszarze: ${formatInteger(response.total)}.`;
}

// ---------------------------------------------------------------------------
// Marker icons (inline SVG data URLs; img-src allows data:). Shape differs by kind so colour is never the only cue.
// ---------------------------------------------------------------------------

function svgUrl(body: string, size: number): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 28 28">${body}</svg>`;
  return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`;
}

/** School: a circle with a small roof. Preschool: a rounded square with a white dot. Both white-outlined. */
export function markerIconUrl(kind: MapMarker["kind"], selected: boolean): string {
  const ring = selected ? `<circle cx="14" cy="14" r="13" fill="none" stroke="#15202b" stroke-width="2"/>` : "";
  if (kind === "school") {
    return svgUrl(
      `${ring}<circle cx="14" cy="14" r="10.5" fill="${MARKER_COLOR.school}" stroke="#ffffff" stroke-width="2.5"/>` +
        `<path d="M8.5 14.6 14 9.6l5.5 5M9.6 14v4.4h8.8V14" fill="none" stroke="#ffffff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>`,
      selected ? 36 : 30,
    );
  }
  return svgUrl(
    `${ring}<rect x="4" y="4" width="20" height="20" rx="5" fill="${MARKER_COLOR.preschool}" stroke="#ffffff" stroke-width="2.5"/>` +
      `<circle cx="14" cy="14" r="3.6" fill="#ffffff"/>`,
    selected ? 36 : 30,
  );
}

export function clusterIconUrl(count: number): string {
  const label = count > 999 ? "999+" : String(count);
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="44" height="44" viewBox="0 0 44 44">` +
    `<circle cx="22" cy="22" r="20" fill="${MARKER_COLOR.cluster}" stroke="#ffffff" stroke-width="3"/>` +
    `<text x="22" y="27" text-anchor="middle" font-family="system-ui,sans-serif" font-size="14" font-weight="700" fill="#ffffff">${label}</text></svg>`;
  return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`;
}
