import type { MetadataRoute } from "next";
import { PRESCHOOL_SITEMAP_ID } from "@/lib/sitemap-config";
import { siteOrigin } from "@/lib/site";

// Read the site origin per request, not at build time.
export const dynamic = "force-dynamic";

export default function robots(): MetadataRoute.Robots {
  const origin = siteOrigin();
  // Next 15 serves each generateSitemaps id as /sitemap/<id>.xml; list them explicitly (no index file).
  const sitemaps = Array.from({ length: PRESCHOOL_SITEMAP_ID + 1 }, (_, id) => `${origin}/sitemap/${id}.xml`);
  return {
    rules: [{ userAgent: "*", allow: "/", disallow: ["/api/"] }],
    sitemap: sitemaps,
  };
}
