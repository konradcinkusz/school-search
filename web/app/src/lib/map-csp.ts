/**
 * Content-Security-Policy assembly (SECURITY-REVIEW section 4, ADR-0022).
 *
 * The policy is built per request in src/middleware.ts, because next.config.ts headers() is evaluated at build time and
 * the Google Maps domains must depend on a runtime variable (GOOGLE_MAPS_API_KEY). Without a key the policy is exactly
 * the strict baseline; with a key only the directives Maps needs are extended, each with named Google hosts.
 * Pure and dependency-free so the middleware (edge runtime) can import it.
 */
export interface CspOptions {
  /** True when a Google Maps key is configured at runtime. */
  readonly googleMaps: boolean;
}

export function buildContentSecurityPolicy(options: CspOptions): string {
  const maps = options.googleMaps;
  const directives: string[] = [
    "default-src 'self'",
    // Next.js inline hydration scripts need 'unsafe-inline' until a nonce-based policy is introduced.
    maps ? "script-src 'self' 'unsafe-inline' https://maps.googleapis.com" : "script-src 'self' 'unsafe-inline'",
    // Maps loads a Roboto stylesheet from fonts.googleapis.com for its controls.
    maps ? "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com" : "style-src 'self' 'unsafe-inline'",
    maps
      ? "img-src 'self' data: blob: https://maps.gstatic.com https://*.gstatic.com https://*.googleapis.com"
      : "img-src 'self' data:",
    // Catalog data still goes through the same-origin BFF; the Google hosts are for map tiles, the Maps API and the Geocoder.
    maps ? "connect-src 'self' https://maps.googleapis.com https://*.googleapis.com" : "connect-src 'self'",
    maps ? "font-src 'self' https://fonts.gstatic.com" : "font-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ];
  if (maps) {
    directives.push("frame-src 'none'", "worker-src 'self' blob:");
  }
  return directives.join("; ");
}
