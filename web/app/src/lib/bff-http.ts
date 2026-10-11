import { NextResponse, type NextRequest } from "next/server";

// Small shared helpers for BFF route handlers: uniform error bodies, bounded JSON reads, no-store caching.

export const NO_STORE_HEADERS = { "Cache-Control": "no-store" } as const;

export function bffError(status: number, code: string, message: string, extra?: Record<string, unknown>): NextResponse {
  return NextResponse.json({ error: { code, message, ...extra } }, { status, headers: NO_STORE_HEADERS });
}

export function bffJson(status: number, body: unknown): NextResponse {
  return NextResponse.json(body, { status, headers: NO_STORE_HEADERS });
}

export function accountsUnavailable(): NextResponse {
  return bffError(503, "auth_unavailable", "Konta są chwilowo niedostępne.");
}

export function reviewsUnavailable(): NextResponse {
  return bffError(503, "reviews_unavailable", "Opinie są chwilowo niedostępne.");
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** Reads a JSON object body with a byte cap. Null for a wrong content type, an oversized body or invalid JSON. */
export async function readJsonObject(request: NextRequest, maxBytes: number): Promise<Record<string, unknown> | null> {
  const contentType = (request.headers.get("content-type") ?? "").toLowerCase();
  if (!contentType.startsWith("application/json")) return null;
  const declared = Number(request.headers.get("content-length") ?? "0");
  if (Number.isFinite(declared) && declared > maxBytes) return null;
  const text = await request.text();
  if (new TextEncoder().encode(text).byteLength > maxBytes) return null;
  try {
    const parsed: unknown = JSON.parse(text);
    return isRecord(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

/** Keeps only short, plain strings from an upstream error list (never HTML, never unbounded). */
export function safeMessages(value: unknown, limit = 5, maxLength = 200): string[] {
  if (!Array.isArray(value)) return [];
  return value
    .filter((item): item is string => typeof item === "string")
    .map((item) => item.trim().slice(0, maxLength))
    .filter((item) => item !== "")
    .slice(0, limit);
}
