import type { MetadataRoute } from "next";
import { siteOrigin } from "@/lib/site";

// Read the site origin per request, not at build time.
export const dynamic = "force-dynamic";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [{ userAgent: "*", allow: "/", disallow: ["/api/"] }],
    sitemap: `${siteOrigin()}/sitemap.xml`,
  };
}
