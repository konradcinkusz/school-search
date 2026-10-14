/**
 * Runtime configuration for the map page (ADR-0022). Read per call from the environment, never inlined at build time
 * (same rule as catalog-url.ts): one image serves every environment, and the key is handed to the browser by
 * /api/config, never through NEXT_PUBLIC_*.
 *
 * A Google Maps browser key is public by design (it is visible in the page source) and is protected by an HTTP
 * referrer restriction and an API restriction set on the key in Google Cloud, not by secrecy. The shape check below
 * exists because the value ends up in a URL and in the decision to relax the Content-Security-Policy.
 */
const KEY_PATTERN = /^[A-Za-z0-9_-]{20,128}$/;

/** The configured key, or null when GOOGLE_MAPS_API_KEY is unset or malformed. Null means: no map, list fallback. */
export function googleMapsApiKey(env: NodeJS.ProcessEnv = process.env): string | null {
  const raw = env.GOOGLE_MAPS_API_KEY?.trim();
  return raw !== undefined && KEY_PATTERN.test(raw) ? raw : null;
}
