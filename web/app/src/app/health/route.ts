export const dynamic = "force-dynamic";

export function GET() {
  return Response.json({ status: "ok", service: "school-search-web" }, { headers: { "Cache-Control": "no-store" } });
}
