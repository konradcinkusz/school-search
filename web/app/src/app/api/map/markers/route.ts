import { NextResponse, type NextRequest } from "next/server";
import { parseMarkerQuery } from "@/lib/map-params";
import { forwardMarkers } from "@/lib/map-proxy";

// BFF route for the map page (ADR-0022): same origin for the browser, so CSP connect-src keeps 'self' for catalog data.
// Dynamic on purpose: the catalog address is read per request.
export const dynamic = "force-dynamic";

export async function GET(request: NextRequest): Promise<NextResponse> {
  const parsed = parseMarkerQuery(request.nextUrl.searchParams);
  if (!parsed.ok) {
    return NextResponse.json(
      { error: { code: "validation_failed", message: parsed.message } },
      { status: 400, headers: { "Cache-Control": "no-store" } },
    );
  }
  return forwardMarkers(request, parsed.upstream);
}
