import { headers } from "next/headers";
import { bffSharedSecret, reviewsBaseUrl } from "@/lib/bff-config";
import type { LegalContact, LegalTerms, ReviewsEnvelope } from "@/lib/reviews-types";
import { isSlug } from "@/lib/validation";

// Server-only reads from the Reviews service for server components. Browser writes go through /api/proxy.
// Visitor address and BFF secret are forwarded the same way the catalog proxy does, so per-IP limits apply to the
// visitor and not to the web server.

const REVIEWS_TIMEOUT_MS = 8_000;

export type ReviewsRead<T> =
  | { readonly ok: true; readonly data: T }
  | { readonly ok: false; readonly reason: "unavailable" | "not_found" | "error"; readonly status: number | null };

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

async function readGet<T>(path: string, query: URLSearchParams | null): Promise<ReviewsRead<T>> {
  const base = reviewsBaseUrl();
  if (base === null) return { ok: false, reason: "unavailable", status: null };
  const suffix = query && query.toString() !== "" ? `?${query.toString()}` : "";
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REVIEWS_TIMEOUT_MS);
  try {
    const response = await fetch(`${base}${path}${suffix}`, {
      method: "GET",
      headers: await visitorHeaders(),
      cache: "no-store",
      signal: controller.signal,
    });
    if (response.status === 404) return { ok: false, reason: "not_found", status: 404 };
    if (!response.ok) {
      console.error(`[reviews] GET ${path} answered ${response.status}`);
      return { ok: false, reason: "error", status: response.status };
    }
    const data = (await response.json()) as T;
    return { ok: true, data };
  } catch {
    console.error(`[reviews] unavailable: GET ${path}`);
    return { ok: false, reason: "unavailable", status: null };
  } finally {
    clearTimeout(timer);
  }
}

export function listSchoolReviews(slug: string, page: number, pageSize: number): Promise<ReviewsRead<ReviewsEnvelope>> {
  if (!isSlug(slug)) return Promise.resolve({ ok: false, reason: "not_found", status: 404 });
  return readGet<ReviewsEnvelope>(
    `/api/v1/schools/${encodeURIComponent(slug)}/reviews`,
    new URLSearchParams({ page: String(page), pageSize: String(pageSize) }),
  );
}

export function legalTerms(): Promise<ReviewsRead<LegalTerms>> {
  return readGet<LegalTerms>("/api/v1/legal/terms", null);
}

export function legalContact(): Promise<ReviewsRead<LegalContact>> {
  return readGet<LegalContact>("/api/v1/legal/contact", null);
}
