import { headers } from "next/headers";
import { billingBaseUrl, bffSharedSecret } from "@/lib/bff-config";
import { parseConsent, parsePlans, parsePremium } from "@/lib/billing-parse";
import type { EnquiryConsent, PlansEnvelope, PremiumEnvelope } from "@/lib/billing-types";
import { isSlug } from "@/lib/validation";

// Server-only reads from the Billing service for server components. Browser writes go through /api/proxy.
// The visitor address and BFF secret are forwarded like the Reviews reads, so per-IP limits apply to the visitor.

const BILLING_TIMEOUT_MS = 8_000;

export type BillingRead<T> =
  | { readonly ok: true; readonly data: T }
  | { readonly ok: false; readonly reason: "unavailable" | "not_found" | "error" | "malformed"; readonly status: number | null };

async function visitorHeaders(): Promise<Record<string, string>> {
  const out: Record<string, string> = { accept: "application/json" };
  const secret = bffSharedSecret();
  if (secret === null) return out;
  const h = await headers();
  const raw = h.get("fly-client-ip") ?? h.get("x-forwarded-for") ?? "";
  const ip = raw.split(",")[0]?.trim() ?? "";
  if (/^[0-9a-fA-F:.]{3,45}$/.test(ip)) {
    out["x-client-ip"] = ip;
    out["x-bff-secret"] = secret;
  }
  return out;
}

async function readGet<T>(path: string, parse: (body: unknown) => T | null): Promise<BillingRead<T>> {
  const base = billingBaseUrl();
  if (base === null) return { ok: false, reason: "unavailable", status: null };
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), BILLING_TIMEOUT_MS);
  try {
    const response = await fetch(`${base}${path}`, {
      method: "GET",
      headers: await visitorHeaders(),
      cache: "no-store",
      signal: controller.signal,
    });
    if (response.status === 404) return { ok: false, reason: "not_found", status: 404 };
    if (!response.ok) {
      console.error(`[billing] GET ${path} answered ${response.status}`);
      return { ok: false, reason: "error", status: response.status };
    }
    const parsed = parse((await response.json()) as unknown);
    if (parsed === null) {
      console.error(`[billing] GET ${path} answered an unexpected shape`);
      return { ok: false, reason: "malformed", status: response.status };
    }
    return { ok: true, data: parsed };
  } catch {
    console.error(`[billing] unavailable: GET ${path}`);
    return { ok: false, reason: "unavailable", status: null };
  } finally {
    clearTimeout(timer);
  }
}

/** Public price list. An empty list means "Cennik w przygotowaniu." (configuration has no plans yet). */
export function listPlans(): Promise<BillingRead<PlansEnvelope>> {
  return readGet<PlansEnvelope>("/api/v1/plans", parsePlans);
}

/** Sponsored block of a school. Anything but a clean answer means: show no sponsored card at all. */
export function schoolPremium(slug: string): Promise<BillingRead<PremiumEnvelope>> {
  if (!isSlug(slug)) return Promise.resolve({ ok: false, reason: "not_found", status: 404 });
  return readGet<PremiumEnvelope>(`/api/v1/schools/${encodeURIComponent(slug)}/premium`, parsePremium);
}

/** Versioned DRAFT consent text for parent enquiries. */
export function enquiryConsent(): Promise<BillingRead<EnquiryConsent>> {
  return readGet<EnquiryConsent>("/api/v1/legal/enquiry-consent", parseConsent);
}
