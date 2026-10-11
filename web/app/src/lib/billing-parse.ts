// Defensive parsers for Billing answers. Browser-safe (no server imports). Anything malformed becomes null or an
// empty list, so the page shows its unavailable state instead of rendering half-read data.
import type {
  BillingPlan,
  EnquiryConsent,
  LeadView,
  ModerationClaimView,
  MyClaimView,
  PlansEnvelope,
  PremiumEnvelope,
  PremiumProfileView,
  ProfileDraft,
  SubscriptionView,
} from "@/lib/billing-types";

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

const str = (value: unknown): string | null => (typeof value === "string" ? value : null);
const bool = (value: unknown): boolean => value === true;

/** Items may arrive as a bare array or as {items: []}. */
function itemsOf(body: unknown): Record<string, unknown>[] | null {
  const list = Array.isArray(body) ? body : isRecord(body) && Array.isArray(body.items) ? body.items : null;
  return list === null ? null : list.filter(isRecord);
}

export function parsePlans(body: unknown): PlansEnvelope | null {
  if (!isRecord(body) || !Array.isArray(body.plans)) return null;
  const plans: BillingPlan[] = [];
  for (const item of body.plans.filter(isRecord)) {
    const code = str(item.code);
    const name = str(item.name);
    if (code === null || name === null) continue;
    const price = typeof item.priceGrossPln === "number" && Number.isFinite(item.priceGrossPln) ? item.priceGrossPln : null;
    plans.push({ code, name, priceGrossPln: price, interval: str(item.interval) ?? "month" });
  }
  return { plans, note: str(body.note), mode: str(body.mode) };
}

function parseProfile(value: unknown): PremiumProfileView | null {
  if (!isRecord(value)) return null;
  const headline = str(value.headline);
  const description = str(value.description);
  if (headline === null || description === null) return null;
  return { headline, description, websiteUrl: str(value.websiteUrl) };
}

export function parsePremium(body: unknown): PremiumEnvelope | null {
  if (!isRecord(body)) return null;
  const tier = str(body.tier);
  if (tier === null) return null;
  return {
    sponsored: true,
    tier,
    editorsVerified: bool(body.editorsVerified),
    profile: parseProfile(body.profile),
    label: str(body.label) ?? "Treść sponsorowana",
    enquiriesOpen: bool(body.enquiriesOpen),
  };
}

export function parseConsent(body: unknown): EnquiryConsent | null {
  if (!isRecord(body)) return null;
  const version = str(body.version);
  const text = str(body.text);
  return version !== null && text !== null && version !== "" && text !== "" ? { version, text } : null;
}

function parseSubscription(value: unknown): SubscriptionView | null {
  if (!isRecord(value)) return null;
  const id = str(value.id);
  if (id === null) return null;
  return {
    id,
    planCode: str(value.planCode) ?? "",
    status: str(value.status) ?? "",
    currentPeriodEnd: str(value.currentPeriodEnd),
    cancelAtPeriodEnd: bool(value.cancelAtPeriodEnd),
  };
}

export function parseMyClaims(body: unknown): MyClaimView[] | null {
  const items = itemsOf(body);
  if (items === null) return null;
  const out: MyClaimView[] = [];
  for (const item of items) {
    const id = str(item.id);
    const schoolSlug = str(item.schoolSlug);
    if (id === null || schoolSlug === null) continue;
    out.push({
      id,
      schoolSlug,
      roleDescription: str(item.roleDescription) ?? "",
      status: str(item.status) ?? "",
      createdAt: str(item.createdAt) ?? "",
      subscription: parseSubscription(item.subscription),
    });
  }
  return out;
}

export function parseLeads(body: unknown): LeadView[] | null {
  const items = itemsOf(body);
  if (items === null) return null;
  const out: LeadView[] = [];
  for (const item of items) {
    const id = str(item.id);
    if (id === null) continue;
    out.push({
      id,
      parentEmail: str(item.parentEmail) ?? "",
      message: str(item.message) ?? "",
      status: str(item.status) ?? "new",
      createdAt: str(item.createdAt) ?? "",
      expiresAt: str(item.expiresAt),
    });
  }
  return out;
}

export function parseModerationClaims(body: unknown): ModerationClaimView[] | null {
  const items = itemsOf(body);
  if (items === null) return null;
  const out: ModerationClaimView[] = [];
  for (const item of items) {
    const id = str(item.id);
    const schoolSlug = str(item.schoolSlug);
    if (id === null || schoolSlug === null) continue;
    out.push({
      id,
      schoolSlug,
      roleDescription: str(item.roleDescription) ?? "",
      status: str(item.status) ?? "",
      createdAt: str(item.createdAt) ?? "",
      notes: str(item.notes),
    });
  }
  return out;
}

export function parseDraft(body: unknown): ProfileDraft | null {
  if (!isRecord(body)) return null;
  const headline = str(body.headline);
  const description = str(body.description);
  return headline !== null && description !== null ? { headline, description } : null;
}
