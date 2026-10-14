import { NextResponse, type NextRequest } from "next/server";
import { buildContentSecurityPolicy } from "@/lib/map-csp";
import { googleMapsApiKey } from "@/lib/map-config";

/**
 * Sets the Content-Security-Policy on every page and route response (SECURITY-REVIEW section 4).
 * It runs per request so the policy can depend on runtime configuration: the Google Maps hosts are allowed only when
 * GOOGLE_MAPS_API_KEY is set (ADR-0022). Without a key the policy is the strict baseline.
 */
export function middleware(_request: NextRequest): NextResponse {
  const response = NextResponse.next();
  response.headers.set(
    "Content-Security-Policy",
    buildContentSecurityPolicy({ googleMaps: googleMapsApiKey() !== null }),
  );
  return response;
}

export const config = {
  // Everything except build assets and the icon; the pages and the /api routes keep the policy.
  matcher: ["/((?!_next/static|_next/image|icon.svg).*)"],
};
