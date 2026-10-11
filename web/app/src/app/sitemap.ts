import type { MetadataRoute } from "next";
import { catalog, type SchoolListItem } from "@/lib/api";
import { siteOrigin } from "@/lib/site";

// Read the site origin and the catalog per request, never at build time.
export const dynamic = "force-dynamic";

/** Sitemap protocol limit per file. */
const MAX_URLS_PER_SITEMAP = 50_000;
/** Catalog page size used for paging through schools. */
const CATALOG_PAGE_SIZE = 100;
/** Pages per sitemap file: 100 x 100 = 10 000 URLs, well under the protocol limit. */
const PAGES_PER_SITEMAP = 100;
const FETCH_CONCURRENCY = 8;

if (CATALOG_PAGE_SIZE * PAGES_PER_SITEMAP > MAX_URLS_PER_SITEMAP) {
  throw new Error("Sitemap chunk exceeds the protocol limit of 50 000 URLs.");
}

const STATIC_PATHS = ["/", "/porownaj", "/metodologia", "/dla-szkol", "/ranking/country"] as const;

/** Id 0 holds the static pages; ids 1..n hold school profiles, n = ceil(pages / PAGES_PER_SITEMAP). */
export async function generateSitemaps(): Promise<Array<{ id: number }>> {
  const first = await catalog.schools({ sort: "name", page: 1, pageSize: CATALOG_PAGE_SIZE });
  const total = first.ok ? first.data.total : 0;
  const pages = Math.ceil(total / CATALOG_PAGE_SIZE);
  const files = Math.ceil(pages / PAGES_PER_SITEMAP);
  return Array.from({ length: files + 1 }, (_, index) => ({ id: index }));
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
    if (seen.has(school.slug) || entries.length >= MAX_URLS_PER_SITEMAP) continue;
    seen.add(school.slug);
    entries.push({
      url: `${origin}/szkoly/${encodeURIComponent(school.slug)}`,
      changeFrequency: "monthly",
    });
  }
  return entries;
}
