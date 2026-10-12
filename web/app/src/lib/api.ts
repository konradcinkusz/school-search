import { catalogCandidates } from "@/lib/catalog-url";

// Server-side client for the public catalog API (SPEC-C public JSON contract).
// Runs only in server components, route handlers and sitemap/robots generators.
// The browser never calls the catalog directly; it goes through /api/proxy (FRONTEND-BFF §5).

/** Per-attempt budget. Matches the proxy's cold-start tolerance order of magnitude. */
export const CATALOG_TIMEOUT_MS = 8_000;
/** One retry covers a scale-to-zero cold start (timeout, network error, 502/503/504). */
const MAX_ATTEMPTS = 2;
const RETRY_DELAY_MS = 1_000;
const RETRYABLE_STATUS = new Set([502, 503, 504]);

// ---------------------------------------------------------------------------
// Contract types (camelCase JSON). Mirror SPEC-C; nullable fields stay nullable.
// ---------------------------------------------------------------------------

/** "limited": 5 to 8 pupils, descriptive data and cohort size only; no score, no interval, no rank (ADR-0009). */
export type ScoreStatus = "published" | "limited" | "insufficient_cohort" | "no_exam_data" | "no_baseline";

export interface ScoreBlock {
  /** Null when suppressed. Never zero for a suppressed score. */
  readonly score: number | null;
  readonly reason: string | null;
  readonly cohortSize: number;
  readonly intervalLow: number | null;
  readonly intervalHigh: number | null;
  readonly methodologyVersion: string;
  readonly examYear: number;
  /** ISO date (yyyy-mm-dd). */
  readonly asOf: string;
  readonly source: string;
}

export interface SchoolListItem {
  readonly slug: string;
  readonly name: string;
  readonly gminaTeryt: string;
  readonly gminaName: string | null;
  readonly isPublic: boolean;
  readonly isSpecial: boolean;
  readonly latitude: number | null;
  readonly longitude: number | null;
  readonly distanceKm: number | null;
  readonly scoreStatus: ScoreStatus;
  readonly score: ScoreBlock | null;
}

export interface GoverningBody {
  readonly typeCode: number | null;
  readonly typeLabel: string | null;
  readonly labelVerified: boolean;
  readonly isNaturalPerson: boolean;
}

export interface Faith {
  readonly derived: boolean;
  readonly declared: boolean | null;
  readonly note: string;
}

export interface CohortRow {
  readonly schoolYear: number;
  readonly classLevel: string;
  readonly pupils: number;
  readonly oddzialy: number;
}

export interface SubjectResult {
  readonly subject: string;
  readonly meanPercent: number | null;
  readonly pupilsSat: number;
}

export interface TrendPoint {
  readonly examYear: number;
  readonly scoreStatus: ScoreStatus;
  readonly score: ScoreBlock | null;
  readonly coveragePercent: number | null;
  readonly subjects: readonly SubjectResult[];
}

export interface ProvenanceRow {
  readonly field: string;
  readonly source: string;
  readonly sourceUrl: string | null;
  readonly asOf: string;
  readonly method: string;
}

export interface SchoolProfile extends SchoolListItem {
  readonly governingBody: GoverningBody;
  readonly faith: Faith;
  readonly cohorts: readonly CohortRow[];
  readonly trend: readonly TrendPoint[];
  readonly provenance: readonly ProvenanceRow[];
  readonly methodologyVersion: string;
}

export interface SchoolPage {
  readonly items: readonly SchoolListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface RankingItem {
  readonly rank: number;
  readonly slug: string;
  readonly name: string;
  readonly gminaName: string | null;
  readonly score: ScoreBlock;
}

export interface RankingPage {
  readonly scope: "country" | "gmina";
  readonly gminaTeryt: string | null;
  readonly examYear: number | null;
  readonly methodologyVersion: string;
  readonly items: readonly RankingItem[];
  readonly unrankedCount: number;
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface Methodology {
  readonly version: string;
  readonly publicationThreshold: number;
  readonly coverageFloor: number;
  readonly weights: string;
  readonly intervalZ: number;
  readonly description: string;
  readonly docsUrl: string;
}

export interface GminaItem {
  readonly teryt: string;
  readonly name: string | null;
  readonly schoolCount: number;
  readonly coverageState: CoverageState;
}

export type CoverageState = "none" | "street_list" | "polygon";

export interface CatchmentResponse {
  readonly gminaTeryt: string;
  readonly coverage: {
    readonly state: CoverageState;
    readonly resolutionUrl: string | null;
    readonly resolutionDate: string | null;
  };
  readonly match: "street_list_match" | "no_match" | "no_number_match" | "no_data";
  readonly schools: readonly SchoolListItem[];
  readonly disclaimer: string;
}

export type CorrectionStatus = "new" | "in_review" | "accepted" | "rejected" | "duplicate";

export interface CorrectionCreated {
  readonly token: string;
  readonly status: CorrectionStatus;
}

export interface CorrectionStatusView {
  readonly token: string;
  readonly status: CorrectionStatus;
  readonly statusReason: string | null;
  readonly createdAt: string;
  readonly schoolSlug: string;
  readonly field: string;
}

// ---------------------------------------------------------------------------
// Preschools (SPEC-D, ADR-0014). Practical facts only: no score, rank, grade or quality field exists on these types.
// Unknown values are null and render as "brak danych", never as zero.
// ---------------------------------------------------------------------------

/** "reported" (primary public source, loaded from file) or "unverified" (submitted, not checked). */
export type PreschoolConfidence = "reported" | "unverified";

export interface FactSource {
  readonly source: string;
  readonly sourceUrl: string | null;
  /** ISO date (yyyy-mm-dd). */
  readonly asOf: string;
  readonly method: string;
  readonly confidence: PreschoolConfidence | string;
}

export interface MoneyRange {
  readonly minPln: number | null;
  readonly maxPln: number | null;
}

export interface OpeningHours {
  /** "HH:mm" or null. */
  readonly opens: string | null;
  /** "HH:mm" or null. */
  readonly closes: string | null;
}

export interface FreePlaces {
  /** Null when unknown; 0 means the unit reported no vacancies. */
  readonly count: number | null;
  /** ISO date (yyyy-mm-dd) the count describes. */
  readonly asOf: string | null;
}

export interface PreschoolFactsBlock {
  readonly monthlyTuition: MoneyRange;
  readonly monthlyMealPln: number | null;
  readonly hours: OpeningHours;
  readonly freePlaces: FreePlaces;
  readonly source: FactSource;
}

export interface PreschoolListItem {
  readonly slug: string;
  readonly name: string;
  readonly gminaTeryt: string | null;
  readonly gminaName: string | null;
  readonly isPublic: boolean;
  readonly latitude: number | null;
  readonly longitude: number | null;
  readonly distanceKm: number | null;
  readonly monthlyTuition: MoneyRange;
  readonly freePlacesCount: number | null;
  /** Same block as the profile's facts, with source, date and confidence. Null when no facts row exists. */
  readonly facts: PreschoolFactsBlock | null;
}

export interface PreschoolProfile {
  readonly slug: string;
  readonly name: string;
  readonly gminaTeryt: string | null;
  readonly gminaName: string | null;
  readonly isPublic: boolean;
  readonly street: string | null;
  readonly postalCode: string | null;
  readonly city: string | null;
  readonly latitude: number | null;
  readonly longitude: number | null;
  readonly governingBody: GoverningBody;
  /** Null when no facts row exists. */
  readonly facts: PreschoolFactsBlock | null;
  /** Fixed API text explaining that preschools have no quality score. */
  readonly notice: string;
}

export interface PreschoolPage {
  readonly items: readonly PreschoolListItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

// ---------------------------------------------------------------------------
// Result type. Pages render a service-unavailable state instead of throwing.
// ---------------------------------------------------------------------------

export type ApiFailureReason = "unavailable" | "not_found" | "bad_request" | "error";

export type ApiResult<T> =
  | { readonly ok: true; readonly data: T }
  | {
      readonly ok: false;
      readonly reason: ApiFailureReason;
      readonly status: number | null;
      /** Server-provided message (API-fed text: render it as text only). */
      readonly message: string | null;
      readonly traceId: string | null;
    };

type CacheMode = { readonly cache: "no-store" } | { readonly next: { readonly revalidate: number } };

const CACHE_SHORT: CacheMode = { next: { revalidate: 120 } };
const CACHE_DEFAULT: CacheMode = { next: { revalidate: 300 } };
const CACHE_LONG: CacheMode = { next: { revalidate: 3600 } };
const CACHE_NONE: CacheMode = { cache: "no-store" };

type RawOutcome = { readonly status: number; readonly body: unknown } | "transient";

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function attemptOnce(url: string, cache: CacheMode): Promise<RawOutcome> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), CATALOG_TIMEOUT_MS);
  try {
    const response = await fetch(url, {
      method: "GET",
      headers: { accept: "application/json" },
      signal: controller.signal,
      ...cache,
    });
    const text = await response.text();
    let body: unknown = null;
    if (text !== "") {
      try {
        body = JSON.parse(text);
      } catch {
        body = null;
      }
    }
    return { status: response.status, body };
  } catch {
    // Abort (timeout) and network errors are both treated as transient.
    return "transient";
  } finally {
    clearTimeout(timer);
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function failureFrom(status: number, body: unknown): ApiResult<never> {
  let message: string | null = null;
  let traceId: string | null = null;
  if (isRecord(body) && isRecord(body.error)) {
    if (typeof body.error.message === "string") message = body.error.message;
    if (typeof body.error.traceId === "string") traceId = body.error.traceId;
  }
  if (status === 404) return { ok: false, reason: "not_found", status, message, traceId };
  if (status === 400) return { ok: false, reason: "bad_request", status, message, traceId };
  return { ok: false, reason: "error", status, message, traceId };
}

/**
 * GET against the catalog through the candidate ladder (catalog-url.ts).
 * 403 means the wrong ingress: next candidate. Timeouts, network errors and 502/503/504 get one retry.
 * Logs carry the path only, never a backend address.
 */
export async function catalogGet<T>(
  path: string,
  query: URLSearchParams | null,
  cache: CacheMode,
): Promise<ApiResult<T>> {
  const suffix = query && query.toString() !== "" ? `?${query.toString()}` : "";

  for (const candidate of catalogCandidates()) {
    const url = `${candidate.url}${path}${suffix}`;
    for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt += 1) {
      const outcome = await attemptOnce(url, cache);
      if (outcome === "transient" || RETRYABLE_STATUS.has(outcome.status)) {
        if (attempt < MAX_ATTEMPTS) {
          await sleep(RETRY_DELAY_MS);
          continue;
        }
        break;
      }
      if (outcome.status === 403) break;
      if (outcome.status >= 200 && outcome.status < 300) {
        if (outcome.body === null) break;
        return { ok: true, data: outcome.body as T };
      }
      return failureFrom(outcome.status, outcome.body);
    }
  }

  console.error(`[catalog] unavailable: GET ${path}`);
  return { ok: false, reason: "unavailable", status: null, message: null, traceId: null };
}

// ---------------------------------------------------------------------------
// Typed endpoints
// ---------------------------------------------------------------------------

export interface SchoolsQuery {
  readonly q?: string;
  readonly gmina?: string;
  readonly isPublic?: boolean;
  readonly lat?: number;
  readonly lon?: number;
  readonly radiusKm?: number;
  readonly sort?: "name" | "score";
  readonly page?: number;
  readonly pageSize?: number;
}

export interface PreschoolsQuery {
  readonly gmina?: string;
  readonly q?: string;
  readonly lat?: number;
  readonly lon?: number;
  readonly radiusKm?: number;
  readonly publicOnly?: boolean;
  readonly maxTuition?: number;
  readonly hasFreePlaces?: boolean;
  readonly page?: number;
  readonly pageSize?: number;
}

function toQuery(entries: Record<string, string | number | boolean | undefined>): URLSearchParams {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(entries)) {
    if (value === undefined || value === "") continue;
    params.set(key, String(value));
  }
  return params;
}

export const catalog = {
  gminas: () => catalogGet<GminaItem[]>("/api/v1/gminas", null, CACHE_DEFAULT),

  schools: (query: SchoolsQuery) =>
    catalogGet<SchoolPage>(
      "/api/v1/schools",
      toQuery({
        q: query.q,
        gmina: query.gmina,
        isPublic: query.isPublic,
        lat: query.lat,
        lon: query.lon,
        radiusKm: query.radiusKm,
        sort: query.sort,
        page: query.page,
        pageSize: query.pageSize,
      }),
      CACHE_SHORT,
    ),

  school: (slug: string) =>
    catalogGet<SchoolProfile>(`/api/v1/schools/${encodeURIComponent(slug)}`, null, CACHE_DEFAULT),

  ranking: (params: {
    readonly scope: "country" | "gmina";
    readonly gmina?: string;
    readonly year?: number;
    readonly page?: number;
    readonly pageSize?: number;
  }) =>
    catalogGet<RankingPage>(
      "/api/v1/ranking",
      toQuery({
        scope: params.scope,
        gmina: params.gmina,
        year: params.year,
        page: params.page,
        pageSize: params.pageSize,
      }),
      CACHE_SHORT,
    ),

  compare: (slugs: readonly string[]) =>
    catalogGet<{ items: SchoolProfile[] }>(
      "/api/v1/compare",
      toQuery({ slugs: slugs.join(",") }),
      CACHE_DEFAULT,
    ),

  methodology: () => catalogGet<Methodology>("/api/v1/methodology", null, CACHE_LONG),

  /** Address lookups are not cached: the street and number space is too large for a useful hit rate. */
  catchment: (params: { readonly gmina: string; readonly street: string; readonly number?: string }) =>
    catalogGet<CatchmentResponse>(
      "/api/v1/catchment",
      toQuery({ gmina: params.gmina, street: params.street, number: params.number }),
      CACHE_NONE,
    ),

  correction: (token: string) =>
    catalogGet<CorrectionStatusView>(
      `/api/v1/corrections/${encodeURIComponent(token)}`,
      null,
      CACHE_NONE,
    ),

  preschools: (query: PreschoolsQuery) =>
    catalogGet<PreschoolPage>(
      "/api/v1/preschools",
      toQuery({
        gmina: query.gmina,
        q: query.q,
        lat: query.lat,
        lon: query.lon,
        radiusKm: query.radiusKm,
        publicOnly: query.publicOnly,
        maxTuition: query.maxTuition,
        hasFreePlaces: query.hasFreePlaces,
        page: query.page,
        pageSize: query.pageSize,
      }),
      CACHE_SHORT,
    ),

  preschool: (slug: string) =>
    catalogGet<PreschoolProfile>(
      `/api/v1/preschools/${encodeURIComponent(slug)}`,
      null,
      CACHE_DEFAULT,
    ),
};

// ---------------------------------------------------------------------------
// AI explain (feature 1, docs/ai/AI-FEATURES.md). Called from the browser through the BFF proxy,
// so these are types only: the client component imports them with `import type` and never this module's runtime.
// ---------------------------------------------------------------------------

export interface ExplainFacts {
  readonly score: ScoreBlock | null;
  readonly scoreStatus: ScoreStatus;
  readonly coveragePercent: number | null;
  readonly cohortSize: number | null;
}

export interface ExplainResponse {
  readonly slug: string;
  /** Model output. Plain text only: render with React escaping, never as HTML. */
  readonly text: string;
  /** Always "Wygenerowano przez AI" from the server. */
  readonly label: string;
  readonly model: string;
  readonly methodologyVersion: string;
  readonly examYear: number;
  readonly facts: ExplainFacts;
  readonly generatedAt: string;
  readonly cached: boolean;
}

/** Uniform error codes from the explain endpoint and the BFF (503 ai_unavailable / ai_budget_exhausted, 502, 429). */
export type ExplainErrorCode =
  | "ai_unavailable"
  | "ai_budget_exhausted"
  | "ai_output_rejected"
  | "rate_limited"
  | "upstream_unavailable"
  | "upstream_timeout"
  | "unknown";

export type ExplainResult =
  | { readonly ok: true; readonly data: ExplainResponse }
  | { readonly ok: false; readonly status: number; readonly code: ExplainErrorCode };
