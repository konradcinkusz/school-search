import { expect, test } from "@playwright/test";

// Pagination (C-05). The name search pages 20 results at a time (web/app/src/app/page.tsx). The demo seed has 14
// schools whose names contain "Przykładowie", so that query fits one page and every page after the first is beyond
// the last page. Only that deterministic behaviour is asserted: the out-of-range message and its way back to page 1.
//
// Gap: no list in the demo seed has more than one page (ranking 50 per page, preschools 20, name search 20, against 14
// schools and 5 preschools), so the "Stronicowanie" controls are never rendered with a next page here. Adding that
// case needs a seed with more rows than a page; until then it is not tested.

const QUERY = "Przykładowie";
const OUT_OF_RANGE = "Na tej stronie nie ma wyników.";
const PAGE_SIZE = 20;

test.describe("name search pagination", () => {
  test("a page beyond the last one says so and links back to page 1", async ({ page }) => {
    await page.goto(`/?q=${encodeURIComponent(QUERY)}&page=2`);
    await page.waitForLoadState("networkidle");

    const count = page.locator(".result-count");
    await expect(count).toBeVisible();
    const total = Number((await count.textContent())?.match(/\d+/)?.[0] ?? NaN);
    expect(total, "demo seed must fit one page for this query").toBeGreaterThan(0);
    expect(total, "demo seed must fit one page for this query").toBeLessThanOrEqual(PAGE_SIZE);

    await expect(page.getByText(OUT_OF_RANGE)).toBeVisible();
    const back = page.getByRole("link", { name: "Wróć do pierwszej strony" });
    await expect(back).toBeVisible();
    const href = await back.getAttribute("href");
    expect(href).not.toBeNull();
    const target = new URL(href!, "http://localhost");
    expect(target.pathname).toBe("/");
    expect(target.searchParams.get("q")).toBe(QUERY);
    expect(target.searchParams.has("page")).toBe(false);
  });

  test("the link back to page 1 shows the results again", async ({ page }) => {
    await page.goto(`/?q=${encodeURIComponent(QUERY)}&page=2`);
    await page.waitForLoadState("networkidle");
    await page.getByRole("link", { name: "Wróć do pierwszej strony" }).click();
    await page.waitForLoadState("networkidle");

    await expect(page).toHaveURL(/\?q=/);
    await expect(page).not.toHaveURL(/page=/);
    await expect(page.getByRole("heading", { level: 2, name: "Wyniki wyszukiwania" })).toBeVisible();
    await expect(page.getByText(OUT_OF_RANGE)).toHaveCount(0);
    await expect(page.locator('a[href="/szkoly/demo-sp-1"]').first()).toBeVisible();
  });

  test("a single-page result shows no pagination controls", async ({ page }) => {
    await page.goto(`/?q=${encodeURIComponent(QUERY)}`);
    await page.waitForLoadState("networkidle");
    await expect(page.getByRole("heading", { level: 2, name: "Wyniki wyszukiwania" })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Stronicowanie" })).toHaveCount(0);
  });
});
