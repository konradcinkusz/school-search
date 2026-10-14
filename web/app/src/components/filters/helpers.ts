// Pure helpers for the filter bar and the list-page wrapper. No React, no DOM: unit-testable.
import {
  filtersToSearchParams,
  hasMapOnlyFilters,
  hasPreschoolOnlyFilters,
  type SearchFilters,
} from "../../lib/search-filters";

export interface ListTarget {
  /** Results page the portal can apply the filters on today. */
  readonly href: string;
  /** Link to the map with every filter, present only when place or radius are set. */
  readonly mapHref: string | null;
  /** Plain-Polish notes about filters the target page does not apply. */
  readonly unapplied: readonly string[];
  /** Notes that explain a routing choice (not a loss of a filter). */
  readonly notes: readonly string[];
}

/**
 * Where "show results" goes from a list page: preschool -> /przedszkola, school or all -> /?q=...
 * Preschool-only filters on kind "all" route to the preschool list. Place and radius are reported honestly and
 * handed to the map. Nothing is silently dropped.
 */
export function listTarget(f: SearchFilters): ListTarget {
  const unapplied: string[] = [];
  const notes: string[] = [];
  let preschool = f.kind === "preschool";
  if (f.kind === "all" && hasPreschoolOnlyFilters(f)) {
    preschool = true;
    notes.push("Czesne i wolne miejsca dotyczą przedszkoli, dlatego pokazujemy listę przedszkoli.");
  }

  const params = new URLSearchParams();
  if (f.gmina) params.set("gmina", f.gmina);
  if (f.q) params.set("q", f.q);
  let base: string;
  if (preschool) {
    base = "/przedszkola";
    if (f.publicOnly) params.set("publicOnly", "true");
    if (f.maxTuitionPln !== undefined) params.set("maxTuition", String(f.maxTuitionPln));
    if (f.hasFreePlaces) params.set("hasFreePlaces", "true");
  } else {
    base = "/";
    if (f.publicOnly) unapplied.push("tylko placówki publiczne (lista szkół ich nie filtruje)");
    if (hasPreschoolOnlyFilters(f)) unapplied.push("czesne i wolne miejsca (dotyczą tylko przedszkoli)");
  }

  let mapHref: string | null = null;
  if (hasMapOnlyFilters(f)) {
    unapplied.push("miejsce i odległość (zastosujemy na mapie)");
    const mapQuery = filtersToSearchParams(f).toString();
    mapHref = mapQuery ? `/mapa?${mapQuery}` : "/mapa";
  }

  const query = params.toString();
  return { href: query ? `${base}?${query}` : base, mapHref, unapplied, notes };
}

export const UNSUPPORTED_PREFIX = "Tego jeszcze nie obsługujemy: ";

export function unsupportedText(items: readonly string[]): string {
  return `${UNSUPPORTED_PREFIX}${items.map((item) => item.replace(/[.\s]+$/, "")).join("; ")}.`;
}
