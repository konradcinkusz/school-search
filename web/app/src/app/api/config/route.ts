import { NextResponse } from "next/server";
import { catalogCandidates } from "@/lib/catalog-url";

// Dynamic on purpose: the environment is read per request so one image serves every environment.
export const dynamic = "force-dynamic";

export function GET() {
  const [first] = catalogCandidates();
  const body = {
    catalogApiUrl: first?.url ?? "http://localhost:8080",
    catalogApiSource: first?.source ?? "default",
  };
  return NextResponse.json(body, {
    headers: { "Cache-Control": "public, max-age=30, stale-while-revalidate=60" },
  });
}
