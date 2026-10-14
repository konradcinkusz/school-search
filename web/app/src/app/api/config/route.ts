import { NextResponse } from "next/server";
import { catalogCandidates } from "@/lib/catalog-url";
import { googleMapsApiKey } from "@/lib/map-config";

// Dynamic on purpose: the environment is read per request so one image serves every environment.
export const dynamic = "force-dynamic";

export function GET() {
  const [first] = catalogCandidates();
  const body: { catalogApiUrl: string; catalogApiSource: string; googleMapsApiKey?: string } = {
    catalogApiUrl: first?.url ?? "http://localhost:8080",
    catalogApiSource: first?.source ?? "default",
  };
  // Present only when GOOGLE_MAPS_API_KEY is set (a referrer-restricted browser key, public by design; ADR-0022).
  const mapsKey = googleMapsApiKey();
  if (mapsKey !== null) body.googleMapsApiKey = mapsKey;
  return NextResponse.json(body, {
    headers: { "Cache-Control": "public, max-age=30, stale-while-revalidate=60" },
  });
}
