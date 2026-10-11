/** Input and API-data validation shared by pages, the proxy and sitemap. Whitelist patterns only. */

export const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
export const TERYT_PATTERN = /^[0-9]{6,7}$/;
/** Correction tokens are random 128-bit values, url-safe base64 (about 22 characters). */
export const TOKEN_PATTERN = /^[A-Za-z0-9_-]{22,128}$/;

export function isSlug(value: string): boolean {
  return value.length <= 120 && SLUG_PATTERN.test(value);
}

export function isTeryt(value: string): boolean {
  return TERYT_PATTERN.test(value);
}

export function isToken(value: string): boolean {
  return TOKEN_PATTERN.test(value);
}

/** Parses "a,b,c" into 2 to 4 distinct valid slugs, or null when the list is not usable. */
export function parseSlugList(raw: string | undefined): string[] | null {
  if (raw === undefined) return null;
  const parts = raw
    .split(",")
    .map((part) => part.trim())
    .filter((part) => part !== "");
  if (parts.length < 2 || parts.length > 4) return null;
  if (!parts.every(isSlug)) return null;
  if (new Set(parts).size !== parts.length) return null;
  return parts;
}

/**
 * API-fed URLs go into href attributes only when they are plain http(s).
 * A javascript: or data: URL from data would otherwise execute on click.
 */
export function safeHttpUrl(raw: string | null | undefined): string | null {
  if (!raw) return null;
  try {
    const parsed = new URL(raw);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return null;
    return parsed.toString();
  } catch {
    return null;
  }
}

/** Accepts only a bounded, single-value search parameter. */
export function firstParam(value: string | string[] | undefined, maxLength: number): string | undefined {
  const raw = Array.isArray(value) ? value[0] : value;
  if (raw === undefined) return undefined;
  const trimmed = raw.trim();
  if (trimmed === "" || trimmed.length > maxLength) return undefined;
  return trimmed;
}

/** Parses a positive amount in złoty ("600", "600,50" or "600.5"). Returns null for anything else. */
export function moneyParam(value: string | string[] | undefined): number | null {
  const raw = firstParam(value, 12);
  if (raw === undefined || !/^[0-9]{1,7}(?:[.,][0-9]{1,2})?$/.test(raw)) return null;
  const parsed = Number.parseFloat(raw.replace(",", "."));
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

/** Parses a positive integer search parameter; anything else falls back to the default. */
export function intParam(value: string | string[] | undefined, fallback: number, max: number): number {
  const raw = firstParam(value, 10);
  if (raw === undefined || !/^[0-9]+$/.test(raw)) return fallback;
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return Math.min(parsed, max);
}
