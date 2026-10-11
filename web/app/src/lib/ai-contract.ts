// Browser-safe contract for the AI endpoints (SPEC-G). Types and tolerant parsers only: every answer is checked
// field by field, and anything unexpected is dropped rather than trusted. Text is rendered by React as plain text.
// Backend shapes are coded against the spec; a missing optional field degrades the view, it never breaks the page.

export const AI_LABEL = "Wygenerowano przez AI";

const TERYT_7 = /^[0-9]{7}$/;
const MAX_LIST = 12;

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function shortStrings(value: unknown, limit: number, maxLength: number): string[] {
  if (!Array.isArray(value)) return [];
  return value
    .filter((item): item is string => typeof item === "string")
    .map((item) => item.trim().slice(0, maxLength))
    .filter((item) => item !== "")
    .slice(0, limit);
}

function labelOf(value: unknown): string {
  return typeof value === "string" && value.trim() !== "" ? value.slice(0, 120) : AI_LABEL;
}

// ---------------------------------------------------------------------------
// Search intent (feature 2): POST /api/v1/ai/search-intent
// ---------------------------------------------------------------------------

export type SchoolKind = "school" | "preschool";

export interface SearchIntentFilters {
  /** Seven-digit TERYT, only when the question named a gmina explicitly. */
  readonly gmina?: string;
  readonly q?: string;
  readonly publicOnly?: boolean;
  /** 0 < value <= 50. */
  readonly maxDistanceKm?: number;
  readonly type?: SchoolKind;
}

export interface SearchIntentResult {
  readonly filters: SearchIntentFilters;
  readonly notes: readonly string[];
  /** Parts of the question the portal cannot answer (for example "najlepsza szkoła"). Never applied as filters. */
  readonly unsupported: readonly string[];
  readonly aiLabel: string;
}

export function parseSearchIntent(payload: unknown): SearchIntentResult | null {
  if (!isRecord(payload)) return null;
  const raw = isRecord(payload.filters) ? payload.filters : {};
  const filters: { -readonly [K in keyof SearchIntentFilters]: SearchIntentFilters[K] } = {};
  if (typeof raw.gmina === "string" && TERYT_7.test(raw.gmina)) filters.gmina = raw.gmina;
  if (typeof raw.q === "string" && raw.q.trim() !== "") filters.q = raw.q.trim().slice(0, 300);
  if (typeof raw.publicOnly === "boolean") filters.publicOnly = raw.publicOnly;
  if (typeof raw.maxDistanceKm === "number" && Number.isFinite(raw.maxDistanceKm) && raw.maxDistanceKm > 0) {
    filters.maxDistanceKm = Math.min(raw.maxDistanceKm, 50);
  }
  if (raw.type === "school" || raw.type === "preschool") filters.type = raw.type;
  return {
    filters,
    notes: shortStrings(raw.notes ?? payload.notes, MAX_LIST, 300),
    unsupported: shortStrings(payload.unsupported, MAX_LIST, 300),
    aiLabel: labelOf(payload.aiLabel),
  };
}

// ---------------------------------------------------------------------------
// Methodology answer (feature 7): POST /api/v1/ai/methodology-answer
// ---------------------------------------------------------------------------

export interface MethodologyAnswer {
  readonly answer: string;
  readonly citedSections: readonly string[];
  readonly aiLabel: string;
  readonly groundedInDocument: boolean;
}

export function parseMethodologyAnswer(payload: unknown): MethodologyAnswer | null {
  if (!isRecord(payload) || typeof payload.answer !== "string" || payload.answer.trim() === "") return null;
  return {
    answer: payload.answer.slice(0, 4000),
    citedSections: shortStrings(payload.citedSections, MAX_LIST, 200),
    aiLabel: labelOf(payload.aiLabel),
    groundedInDocument: payload.groundedInDocument !== false,
  };
}

// ---------------------------------------------------------------------------
// Advisor agent (feature G6): POST /api/agent (BFF) -> MCP service POST /agent/chat
// ---------------------------------------------------------------------------

export interface AgentStep {
  readonly tool: string;
  readonly summary: string;
}

export interface AgentReply {
  readonly reply: string;
  readonly steps: readonly AgentStep[];
  readonly citedSlugs: readonly string[];
  readonly aiLabel: string;
  readonly stopReason: string;
}

export function parseAgentReply(payload: unknown): AgentReply | null {
  if (!isRecord(payload) || typeof payload.reply !== "string" || payload.reply.trim() === "") return null;
  const steps: AgentStep[] = [];
  if (Array.isArray(payload.steps)) {
    for (const item of payload.steps.slice(0, 16)) {
      if (!isRecord(item) || typeof item.summary !== "string") continue;
      steps.push({
        tool: typeof item.tool === "string" ? item.tool.slice(0, 60) : "",
        summary: item.summary.slice(0, 400),
      });
    }
  }
  return {
    reply: payload.reply.slice(0, 4000),
    steps,
    citedSlugs: shortStrings(payload.citedSlugs, 20, 120).filter((slug) => /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug)),
    aiLabel: labelOf(payload.aiLabel),
    stopReason: typeof payload.stopReason === "string" ? payload.stopReason.slice(0, 60) : "end_turn",
  };
}

// ---------------------------------------------------------------------------
// Corrections for moderators (feature 5): GET /api/v1/admin/corrections and POST .../{id}/triage
// ---------------------------------------------------------------------------

export interface AdminCorrection {
  readonly id: string;
  readonly schoolSlug: string | null;
  readonly field: string;
  readonly message: string;
  readonly contactEmail: string | null;
  readonly status: string;
  readonly createdAt: string;
}

export interface AdminCorrectionPage {
  readonly items: readonly AdminCorrection[];
  readonly total: number;
}

export function parseAdminCorrections(payload: unknown): AdminCorrectionPage | null {
  if (!isRecord(payload) || !Array.isArray(payload.items)) return null;
  const items: AdminCorrection[] = [];
  for (const item of payload.items) {
    if (!isRecord(item) || typeof item.id !== "string" || typeof item.message !== "string") continue;
    items.push({
      id: item.id,
      schoolSlug: typeof item.schoolSlug === "string" ? item.schoolSlug : null,
      field: typeof item.field === "string" ? item.field : "other",
      message: item.message,
      contactEmail: typeof item.contactEmail === "string" ? item.contactEmail : null,
      status: typeof item.status === "string" ? item.status : "new",
      createdAt: typeof item.createdAt === "string" ? item.createdAt : "",
    });
  }
  return {
    items,
    total: typeof payload.total === "number" ? payload.total : items.length,
  };
}

export interface CorrectionTriage {
  readonly category: string;
  readonly fieldLooksValid: boolean | null;
  readonly severity: string;
  readonly suggestedAction: string;
  readonly draftReplyPl: string;
  readonly aiLabel: string;
}

export function parseCorrectionTriage(payload: unknown): CorrectionTriage | null {
  if (!isRecord(payload)) return null;
  const draft = typeof payload.draftReplyPl === "string" ? payload.draftReplyPl : "";
  return {
    category: typeof payload.category === "string" ? payload.category.slice(0, 80) : "",
    fieldLooksValid: typeof payload.fieldLooksValid === "boolean" ? payload.fieldLooksValid : null,
    severity: typeof payload.severity === "string" ? payload.severity.slice(0, 40) : "",
    suggestedAction: typeof payload.suggestedAction === "string" ? payload.suggestedAction.slice(0, 300) : "",
    draftReplyPl: draft.slice(0, 4000),
    aiLabel: labelOf(payload.aiLabel),
  };
}

// ---------------------------------------------------------------------------
// Reviews: owner reply (right of reply) and complaint decision
// ---------------------------------------------------------------------------

export interface SchoolReviewForOwner {
  readonly id: string;
  readonly rating: number;
  readonly title: string;
  readonly body: string;
  readonly hasReply: boolean;
}

export function parseSchoolReviewsForOwner(payload: unknown): SchoolReviewForOwner[] | null {
  if (!isRecord(payload) || !Array.isArray(payload.items)) return null;
  const out: SchoolReviewForOwner[] = [];
  for (const item of payload.items) {
    if (!isRecord(item) || typeof item.id !== "string") continue;
    out.push({
      id: item.id,
      rating: typeof item.rating === "number" ? item.rating : 0,
      title: typeof item.title === "string" ? item.title : "",
      body: typeof item.body === "string" ? item.body : "",
      hasReply: isRecord(item.reply),
    });
  }
  return out;
}
