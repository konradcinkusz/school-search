import { expect, test } from "@playwright/test";

// Crawlability and metadata (C-04). Uses the API request fixture for robots.txt and the sitemap files, and the page for
// titles and structured data. Origins are not asserted: SITE_URL decides them per environment, so only paths and the
// sitemap file ids are checked. The ids follow SCHOOL_SITEMAP_FILES (5) and PRESCHOOL_SITEMAP_ID (6), see sitemap-config.ts.

const SITEMAP_IDS = [0, 1, 2, 3, 4, 5, 6] as const;
const STATIC_PATHS = ["/", "/porownaj", "/metodologia", "/dla-szkol", "/ranking/country", "/przedszkola", "/mapa"] as const;
const TITLE_ROUTES = [
  "/",
  "/szkoly/demo-sp-1",
  "/ranking/country",
  "/metodologia",
  "/przedszkola",
  "/przedszkola/przedszkole-demonstracyjne-nr-1",
  "/dla-szkol",
  "/doradca",
] as const;
const NOT_FOUND_TITLE_PREFIX = "Nie znaleziono strony | ";

/** The <loc> values of a sitemap XML document. */
function locations(xml: string): string[] {
  return Array.from(xml.matchAll(/<loc>([^<]+)<\/loc>/g), (match) => match[1]);
}

test.describe("robots.txt", () => {
  test("disallows /api/ and lists every sitemap file", async ({ request }) => {
    const response = await request.get("/robots.txt");
    expect(response.status()).toBe(200);
    const body = await response.text();

    expect(body).toMatch(/^User-Agent: \*$/m);
    expect(body).toMatch(/^Disallow: \/api\/$/m);

    const sitemapLines = body
      .split("\n")
      .filter((line) => line.startsWith("Sitemap: "))
      .map((line) => line.slice("Sitemap: ".length).trim());
    const ids = sitemapLines.map((url) => {
      const match = url.match(/\/sitemap\/(\d+)\.xml$/);
      expect(match, `sitemap line is not /sitemap/<id>.xml: ${url}`).not.toBeNull();
      return Number(match![1]);
    });
    expect(ids).toEqual([...SITEMAP_IDS]);
  });
});

test.describe("sitemap files", () => {
  test("the static file lists the public static pages", async ({ request }) => {
    const response = await request.get("/sitemap/0.xml");
    expect(response.status()).toBe(200);
    const paths = locations(await response.text()).map((url) => new URL(url).pathname);
    expect(paths).toEqual(expect.arrayContaining([...STATIC_PATHS]));
    expect(paths).toHaveLength(STATIC_PATHS.length);
  });

  test("the school file lists the demo school profiles", async ({ request }) => {
    const response = await request.get("/sitemap/1.xml");
    expect(response.status()).toBe(200);
    const paths = locations(await response.text()).map((url) => new URL(url).pathname);
    expect(paths).toEqual(expect.arrayContaining(["/szkoly/demo-sp-1", "/szkoly/demo-sp-14"]));
  });

  test("the preschool file lists the demo preschool profiles", async ({ request }) => {
    const response = await request.get("/sitemap/6.xml");
    expect(response.status()).toBe(200);
    const paths = locations(await response.text()).map((url) => new URL(url).pathname);
    expect(paths).toContain("/przedszkola/przedszkole-demonstracyjne-nr-1");
  });
});

test.describe("page metadata", () => {
  test("a school profile carries parseable schema.org School JSON-LD", async ({ page }) => {
    await page.goto("/szkoly/demo-sp-1");
    const blocks = await page.locator('script[type="application/ld+json"]').allTextContents();
    expect(blocks.length).toBeGreaterThan(0);

    const parsed = blocks.map((text) => JSON.parse(text) as Record<string, unknown>);
    const school = parsed.find((item) => item["@type"] === "School");
    expect(school, `no School block in ${JSON.stringify(parsed)}`).toBeDefined();
    expect(school!["@context"]).toBe("https://schema.org");
    expect(typeof school!.name).toBe("string");
    expect(String(school!.url)).toMatch(/\/szkoly\/demo-sp-1$/);
  });

  test("titles are non-empty and unique across eight routes", async ({ page }) => {
    const titles: string[] = [];
    for (const route of TITLE_ROUTES) {
      await page.goto(route);
      const title = (await page.title()).trim();
      expect(title, `empty title on ${route}`).not.toBe("");
      titles.push(title);
    }
    expect(new Set(titles).size, `duplicate titles: ${JSON.stringify(titles)}`).toBe(titles.length);
  });

  test("an unknown path answers 404 with the not-found title", async ({ page }) => {
    const response = await page.goto("/strona-ktorej-nie-ma");
    expect(response?.status()).toBe(404);
    const title = await page.title();
    expect(title.startsWith(NOT_FOUND_TITLE_PREFIX), `title was "${title}"`).toBe(true);
    expect(title.length).toBeGreaterThan(NOT_FOUND_TITLE_PREFIX.length);
    await expect(page.getByRole("heading", { level: 1, name: "Nie znaleziono strony" })).toBeVisible();
  });
});

test.describe("Open Graph and canonical", () => {
  const OG_ROUTES = ["/metodologia", "/szkoly/demo-sp-1", "/ranking/country"] as const;

  for (const route of OG_ROUTES) {
    test(`${route} carries og:title, og:description and og:type website`, async ({ page }) => {
      await page.goto(route);
      const read = (property: string) =>
        page.locator(`meta[property="${property}"]`).first().getAttribute("content");

      expect((await read("og:title"))?.trim(), `og:title on ${route}`).toBeTruthy();
      expect((await read("og:description"))?.trim(), `og:description on ${route}`).toBeTruthy();
      expect(await read("og:type"), `og:type on ${route}`).toBe("website");
    });
  }

  test("the home page has a canonical link to the root path", async ({ page }) => {
    await page.goto("/");
    const href = await page.locator('link[rel="canonical"]').first().getAttribute("href");
    expect(href, "home canonical link").not.toBeNull();
    expect(new URL(href!).pathname).toBe("/");
  });
});
