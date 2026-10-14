// Shared filter model for the list pages, the AI filter bar and the map page (Phase M, contract section 3).
// Pure and browser-safe: no imports, no DOM. The model never carries a score, a rank or a sort order.
// Exports from the contract are fixed (additive extension only): FacilityKind, SearchFilters, EMPTY_FILTERS,
// filtersToSearchParams, filtersFromSearchParams, describeFilters. The helpers below them are additive.

export type FacilityKind = "school" | "preschool" | "all";

export interface SearchFilters {
  kind: FacilityKind;
  /** Name substring. */
  q?: string;
  /** Seven-digit TERYT of a gmina. */
  gmina?: string;
  publicOnly?: boolean;
  /** Preschool only: monthly tuition ceiling in PLN, integer. */
  maxTuitionPln?: number;
  /** Preschool only. */
  hasFreePlaces?: boolean;
  /** Free-text place phrase ("Plac Wolności w Poznaniu"). Never coordinates. Used to pan the map, never for a catchment. */
  place?: string;
  /** Radius around the place, in km. Map only. */
  radiusKm?: number;
}

export const EMPTY_FILTERS: SearchFilters = { kind: "all" };

export const FILTER_LIMITS = {
  qMax: 100,
  placeMax: 120,
  tuitionMin: 1,
  tuitionMax: 20_000,
  radiusMin: 0.5,
  radiusMax: 50,
} as const;

/** URL parameter names. They equal the names the preschool page and the map markers API already use. */
export const PARAM = {
  kind: "kind",
  q: "q",
  gmina: "gmina",
  publicOnly: "publicOnly",
  maxTuitionPln: "maxTuition",
  hasFreePlaces: "hasFreePlaces",
  place: "place",
  radiusKm: "radiusKm",
} as const;

const TERYT_7 = /^[0-9]{7}$/;
const EMAIL_LIKE = /\S+@\S+/;
const LONG_DIGITS = /[0-9](?:[\s-]?[0-9]){8,}/;
const COORDINATE_PAIR = /-?[0-9]{1,3}[.,][0-9]+\s*[,;/ ]\s*-?[0-9]{1,3}[.,][0-9]+/;
// eslint-disable-next-line no-control-regex
const CONTROL_CHARS = /[\u0000-\u001f\u007f]/;

// ---------------------------------------------------------------------------
// Field sanitisers. Each returns undefined for anything unusable: junk is dropped, never guessed.
// ---------------------------------------------------------------------------

export function sanitizeKind(value: unknown): FacilityKind {
  return value === "school" || value === "preschool" ? value : "all";
}

export function sanitizeText(value: unknown, max: number): string | undefined {
  if (typeof value !== "string") return undefined;
  const text = value.replace(/\s+/g, " ").trim();
  if (text === "" || CONTROL_CHARS.test(text)) return undefined;
  return text.slice(0, max);
}

export function sanitizeGmina(value: unknown): string | undefined {
  return typeof value === "string" && TERYT_7.test(value.trim()) ? value.trim() : undefined;
}

/** A place phrase: no e-mail, no long digit runs (phones, PESEL), no coordinate pairs. */
export function sanitizePlace(value: unknown): string | undefined {
  const text = sanitizeText(value, FILTER_LIMITS.placeMax);
  if (text === undefined) return undefined;
  if (EMAIL_LIKE.test(text) || LONG_DIGITS.test(text) || COORDINATE_PAIR.test(text)) return undefined;
  return text;
}

/** Integer PLN in 1..20000. Accepts numbers and numeric strings ("600", "600,50"); decimals are floored. */
export function sanitizeTuition(value: unknown): number | undefined {
  let n: number;
  if (typeof value === "number") n = value;
  else if (typeof value === "string" && /^[0-9]{1,5}(?:[.,][0-9]{1,2})?$/.test(value.trim())) {
    n = Number.parseFloat(value.trim().replace(",", "."));
  } else return undefined;
  if (!Number.isFinite(n)) return undefined;
  const whole = Math.floor(n);
  return whole >= FILTER_LIMITS.tuitionMin && whole <= FILTER_LIMITS.tuitionMax ? whole : undefined;
}

/** Radius in km, 0.5..50, one decimal. Values above the maximum are rejected (not clamped): the visitor decides. */
export function sanitizeRadius(value: unknown): number | undefined {
  let n: number;
  if (typeof value === "number") n = value;
  else if (typeof value === "string" && /^[0-9]{1,2}(?:[.,][0-9]{1,2})?$/.test(value.trim())) {
    n = Number.parseFloat(value.trim().replace(",", "."));
  } else return undefined;
  if (!Number.isFinite(n)) return undefined;
  const rounded = Math.round(n * 10) / 10;
  return rounded >= FILTER_LIMITS.radiusMin && rounded <= FILTER_LIMITS.radiusMax ? rounded : undefined;
}

function truthy(value: unknown): boolean {
  return value === true || value === "true" || value === "1";
}

/** Validates every field of a loosely typed object. Used by the URL reader and by the AI mapping. */
export function normalizeFilters(raw: Partial<Record<keyof SearchFilters, unknown>>): SearchFilters {
  const out: SearchFilters = { kind: sanitizeKind(raw.kind) };
  const q = sanitizeText(raw.q, FILTER_LIMITS.qMax);
  if (q !== undefined) out.q = q;
  const gmina = sanitizeGmina(raw.gmina);
  if (gmina !== undefined) out.gmina = gmina;
  if (truthy(raw.publicOnly)) out.publicOnly = true;
  const tuition = sanitizeTuition(raw.maxTuitionPln);
  if (tuition !== undefined) out.maxTuitionPln = tuition;
  if (truthy(raw.hasFreePlaces)) out.hasFreePlaces = true;
  const place = sanitizePlace(raw.place);
  if (place !== undefined) out.place = place;
  const radius = sanitizeRadius(raw.radiusKm);
  if (radius !== undefined) out.radiusKm = radius;
  return out;
}

// ---------------------------------------------------------------------------
// URL round trip
// ---------------------------------------------------------------------------

/** Omits empty values; `kind` is omitted when "all"; booleans are written only when true. */
export function filtersToSearchParams(f: SearchFilters): URLSearchParams {
  const params = new URLSearchParams();
  const n = normalizeFilters(f as unknown as Partial<Record<keyof SearchFilters, unknown>>);
  if (n.kind !== "all") params.set(PARAM.kind, n.kind);
  if (n.q !== undefined) params.set(PARAM.q, n.q);
  if (n.gmina !== undefined) params.set(PARAM.gmina, n.gmina);
  if (n.publicOnly) params.set(PARAM.publicOnly, "true");
  if (n.maxTuitionPln !== undefined) params.set(PARAM.maxTuitionPln, String(n.maxTuitionPln));
  if (n.hasFreePlaces) params.set(PARAM.hasFreePlaces, "true");
  if (n.place !== undefined) params.set(PARAM.place, n.place);
  if (n.radiusKm !== undefined) params.set(PARAM.radiusKm, String(n.radiusKm));
  return params;
}

function firstValue(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** Tolerant reader for a URLSearchParams or a Next.js searchParams record. Unknown keys and junk values are ignored. */
export function filtersFromSearchParams(p: URLSearchParams | Record<string, string | string[] | undefined>): SearchFilters {
  const get = (key: string): string | undefined => {
    if (p instanceof URLSearchParams) return p.get(key) ?? undefined;
    return firstValue(p[key]);
  };
  return normalizeFilters({
    kind: get(PARAM.kind),
    q: get(PARAM.q),
    gmina: get(PARAM.gmina),
    publicOnly: get(PARAM.publicOnly),
    maxTuitionPln: get(PARAM.maxTuitionPln) ?? get("maxTuitionPln"),
    hasFreePlaces: get(PARAM.hasFreePlaces),
    place: get(PARAM.place),
    radiusKm: get(PARAM.radiusKm),
  });
}

// ---------------------------------------------------------------------------
// Chips
// ---------------------------------------------------------------------------

function formatKm(km: number): string {
  return String(km).replace(".", ",");
}

/** Polish labels for removable chips, in a fixed order. `kind` has no chip when "all". */
export function describeFilters(f: SearchFilters): { key: keyof SearchFilters; label: string }[] {
  const n = normalizeFilters(f as unknown as Partial<Record<keyof SearchFilters, unknown>>);
  const chips: { key: keyof SearchFilters; label: string }[] = [];
  if (n.kind === "school") chips.push({ key: "kind", label: "Rodzaj: szkoła" });
  if (n.kind === "preschool") chips.push({ key: "kind", label: "Rodzaj: przedszkole" });
  if (n.q !== undefined) chips.push({ key: "q", label: `Nazwa: „${n.q}”` });
  if (n.gmina !== undefined) chips.push({ key: "gmina", label: `Gmina (TERYT): ${n.gmina}` });
  if (n.publicOnly) chips.push({ key: "publicOnly", label: "Tylko publiczne" });
  if (n.maxTuitionPln !== undefined) chips.push({ key: "maxTuitionPln", label: `Czesne do ${n.maxTuitionPln} zł` });
  if (n.hasFreePlaces) chips.push({ key: "hasFreePlaces", label: "Są wolne miejsca" });
  if (n.place !== undefined) chips.push({ key: "place", label: `Okolica: ${n.place}` });
  if (n.radiusKm !== undefined) chips.push({ key: "radiusKm", label: `Do ${formatKm(n.radiusKm)} km` });
  return chips;
}

// ---------------------------------------------------------------------------
// Additive helpers
// ---------------------------------------------------------------------------

/** A copy without one filter (kind resets to "all"). */
export function removeFilter(f: SearchFilters, key: keyof SearchFilters): SearchFilters {
  if (key === "kind") return { ...f, kind: "all" };
  const next: SearchFilters = { ...f };
  delete next[key];
  return next;
}

export function hasActiveFilters(f: SearchFilters): boolean {
  return describeFilters(f).length > 0;
}

/** Filters that only make sense for preschools. */
export function hasPreschoolOnlyFilters(f: SearchFilters): boolean {
  return f.maxTuitionPln !== undefined || f.hasFreePlaces === true;
}

/** Filters the list pages cannot apply: they need the map. */
export function hasMapOnlyFilters(f: SearchFilters): boolean {
  return f.place !== undefined || f.radiusKm !== undefined;
}
