import type { MetadataRoute } from "next";
import { catalog, type SchoolListItem } from "@/lib/api";
import { siteOrigin } from "@/lib/site";
import { isSlug } from "@/lib/validation";
import { SCHOOL_SITEMAP_FILES } from "@/lib/sitemap-config";

// Read the site origin and the catalog per request, never at build time.
export const dynamic = "force-dynamic";

/** Sitemap protocol limit per file. */
const MAX_URLS_PER_SITEMAP = 50_000;
/** Catalog page size used for paging through schools. */
const CATALOG_PAGE_SIZE = 100;
/** Pages per sitemap file: 100 x 100 = 10 000 URLs, well under the protocol limit. */
const PAGES_PER_SITEMAP = 100;
const FETCH_CONCURRENCY = 8;

const STATIC_PATHS = ["/", "/porownaj", "/metodologia", "/dla-szkol", "/ranking/country"] as const;

/** Id 0 holds the static pages; ids 1..SCHOOL_SITEMAP_FILES hold school profiles (see sitemap-config.ts). */
export async function generateSitemaps(): Promise<Array<{ id: number }>> {
  return Array.from({ length: SCHOOL_SITEMAP_FILES + 1 }, (_, index) => ({ id: index }));
}

async function schoolPage(page: number): Promise<SchoolListItem[]> {
  const result = await catalog.schools({ sort: "name", page, pageSize: CATALOG_PAGE_SIZE });
  return result.ok ? [...result.data.items] : [];
}

/** Fetches pages with bounded concurrency so one sitemap does not flood the catalog. */
async function schoolsForChunk(firstPage: number, lastPage: number): Promise<SchoolListItem[]> {
  const pages: number[] = [];
  for (let page = firstPage; page <= lastPage; page += 1) pages.push(page);
  const collected: SchoolListItem[] = [];
  for (let i = 0; i < pages.length; i += FETCH_CONCURRENCY) {
    const batch = pages.slice(i, i + FETCH_CONCURRENCY);
    const results = await Promise.all(batch.map((page) => schoolPage(page)));
    for (const items of results) collected.push(...items);
  }
  return collected;
}

export default async function sitemap({ id }: { id: number | Promise<string> }): Promise<MetadataRoute.Sitemap> {
  const origin = siteOrigin();
  const numericId = Number(await id);
  if (!Number.isInteger(numericId) || numericId < 0) return [];

  if (numericId === 0) {
    return STATIC_PATHS.map((path) => ({ url: `${origin}${path}`, changeFrequency: "weekly" as const }));
  }

  const firstPage = (numericId - 1) * PAGES_PER_SITEMAP + 1;
  const lastPage = numericId * PAGES_PER_SITEMAP;
  const schools = await schoolsForChunk(firstPage, lastPage);
  const seen = new Set<string>();
  const entries: MetadataRoute.Sitemap = [];
  for (const school of schools) {
    if (!isSlug(school.slug) || seen.has(school.slug) || entries.length >= MAX_URLS_PER_SITEMAP) continue;
    seen.add(school.slug);
    entries.push({
      url: `${origin}/szkoly/${encodeURIComponent(school.slug)}`,
      changeFrequency: "monthly",
    });
  }
  return entries;
}
