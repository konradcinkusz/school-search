import type { MetadataRoute } from "next";
import { siteOrigin } from "@/lib/site";
import { ZASIEG_VALUES } from "@/lib/zasieg";

// Read the site origin per request, not at build time.
export const dynamic = "force-dynamic";

export default function sitemap(): MetadataRoute.Sitemap {
  const origin = siteOrigin();
  const paths = [
    "/",
    "/porownaj",
    "/metodologia",
    "/dla-szkol",
    ...ZASIEG_VALUES.map((zasieg) => `/ranking/${zasieg}`),
  ];
  return paths.map((path) => ({ url: `${origin}${path}`, changeFrequency: "weekly" }));
}
