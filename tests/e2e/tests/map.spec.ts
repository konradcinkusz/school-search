import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

// Map page (Phase M, ADR-0022). CI runs without GOOGLE_MAPS_API_KEY, so these tests exercise the graceful fallback
// (P8): the same filters and the same results as a list, plus the Polish notice that the map is unavailable. Google is
// never contacted. The BFF route /api/map/markers is tested through HTTP, and the error and truncation states with
// page.route stubs. If a key IS configured in the environment under test, the fallback assertions are skipped by a
// beforeEach hook (the map itself needs a real Google key and is checked by hand, see ADR-0022).

const NOTICE = "Mapa jest niedostępna: nie skonfigurowano klucza Google Maps. Wyniki są pokazane jako lista.";
const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21aa"] as const;
const BLOCKING_IMPACTS = new Set(["serious", "critical"]);
const BBOX = "south=48.9&west=13.9&north=55&east=24.3";

async function openMap(page: Page, query = ""): Promise<void> {
  await page.goto(`/mapa${query}`);
  await page.waitForLoadState("networkidle");
}

async function hasMapsKey(page: Page): Promise<boolean> {
  const response = await page.request.get("/api/config");
  const body = (await response.json()) as { googleMapsApiKey?: string };
  return typeof body.googleMapsApiKey === "string";
}

test.describe("map page: no Google Maps key (list fallback)", () => {
  test.beforeEach(async ({ page }) => {
    test.skip(await hasMapsKey(page), "a Google Maps key is configured: the fallback is not shown");
  });

  test("shows the unavailable notice, the catchment help text and a result list with profile links", async ({ page }) => {
    await openMap(page);
    const main = page.getByRole("main");
    await expect(main.getByRole("heading", { level: 1, name: "Mapa szkół i przedszkoli" })).toBeVisible();
    await expect(main.getByText(NOTICE)).toBeVisible();
    await expect(
      main.getByText("Mapa służy do przeglądania okolicy. Obwód szkolny sprawdzisz po adresie na stronie głównej."),
    ).toBeVisible();
    await expect(main.getByRole("status").filter({ hasText: /Placówek w tym obszarze: \d+/ })).toBeVisible();

    const school = main.getByRole("link", { name: /Szkoła Podstawowa nr 1 w Przykładowie/ }).first();
    await expect(school).toHaveAttribute("href", "/szkoly/demo-sp-1");
    await expect(main.locator('a[href="/przedszkola/przedszkole-demonstracyjne-nr-1"]')).toHaveCount(1);
    // The map toggle exists only when a map does.
    await expect(main.getByRole("button", { name: "Mapa", exact: true })).toHaveCount(0);
  });

  test("preschool facts read 'brak danych' when a value is unknown, and never show a score", async ({ page }) => {
    await openMap(page, "?kind=preschool");
    const main = page.getByRole("main");
    const items = main.locator("li.map-list__item");
    await expect(items).toHaveCount(5);
    await expect(items.filter({ hasText: "brak danych" }).first()).toBeVisible();
    const text = (await main.locator(".map-list").innerText()).toLowerCase();
    expect(text).not.toMatch(/najlepsz|najgorsz|ranking|wynik|punkt/);
  });

  test("the kind filter in the URL narrows the list, and every link points to the matching profile route", async ({ page }) => {
    await openMap(page, "?kind=school");
    const hrefs = await page.getByRole("main").locator(".map-list a").evaluateAll((links) =>
      links.map((link) => link.getAttribute("href") ?? ""),
    );
    expect(hrefs.length).toBeGreaterThan(0);
    for (const href of hrefs) expect(href).toMatch(/^\/szkoly\/[a-z0-9-]+$/);

    await openMap(page, "?kind=preschool&hasFreePlaces=true");
    const preschoolHrefs = await page.getByRole("main").locator(".map-list a").evaluateAll((links) =>
      links.map((link) => link.getAttribute("href") ?? ""),
    );
    expect(preschoolHrefs.length).toBeGreaterThan(0);
    for (const href of preschoolHrefs) expect(href).toMatch(/^\/przedszkola\/[a-z0-9-]+$/);
  });

  test("editing a filter by hand refreshes the list and keeps the URL shareable", async ({ page }) => {
    await openMap(page);
    const main = page.getByRole("main");
    await main.getByRole("button", { name: "Ustaw filtry ręcznie" }).click();
    await main.getByLabel("Rodzaj placówki").selectOption("preschool");

    await expect(page).toHaveURL(/[?&]kind=preschool/);
    await expect(main.locator("li.map-list__item")).toHaveCount(5);
    await expect(main.getByRole("list", { name: "Aktywne filtry" })).toContainText("Rodzaj: przedszkole");

    await main.getByRole("button", { name: "Usuń filtr: Rodzaj: przedszkole" }).click();
    await expect(page).not.toHaveURL(/kind=/);
    await expect(main.locator("li.map-list__item")).toHaveCount(19);
  });

  test("a truncated answer shows the narrowing notice", async ({ page }) => {
    await page.route("**/api/map/markers?*", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          items: [
            {
              kind: "school",
              slug: "demo-sp-1",
              name: "Szkoła testowa",
              latitude: 52,
              longitude: 19,
              isPublic: true,
              gminaName: "Przykładowo",
              monthlyTuition: null,
              freePlacesCount: null,
            },
          ],
          total: 900,
          truncated: true,
          limit: 1,
        }),
      }),
    );
    await openMap(page);
    const main = page.getByRole("main");
    await expect(main.getByText("Pokazano tylko część placówek. Zawęź filtry, aby zobaczyć wszystkie.")).toBeVisible();
    await expect(main.getByRole("status").filter({ hasText: "Placówek w tym obszarze: 900. Pokazano 1." })).toBeVisible();
  });

  test("an upstream failure is reported in Polish and the page stays usable", async ({ page }) => {
    await page.route("**/api/map/markers?*", (route) =>
      route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ error: { code: "upstream_unavailable", message: "Usługa katalogu jest chwilowo niedostępna." } }),
      }),
    );
    await openMap(page);
    const main = page.getByRole("main");
    await expect(main.getByRole("status").filter({ hasText: "Usługa katalogu jest chwilowo niedostępna." })).toBeVisible();
    await expect(main.getByRole("heading", { level: 2, name: "Opisz czego szukasz" })).toBeVisible();
  });

  test("the response carries the strict baseline Content-Security-Policy (no Google hosts)", async ({ page }) => {
    const response = await page.goto("/mapa");
    const csp = response?.headers()["content-security-policy"] ?? "";
    expect(csp).toContain("connect-src 'self'");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).not.toContain("googleapis.com");
    expect(csp).not.toContain("gstatic.com");
  });
});

test.describe("map page: Google cannot be loaded", () => {
  test("a key whose script cannot load falls back to the list with a Polish message", async ({ page }) => {
    // A syntactically valid key is injected into the runtime config. Google is never reachable from CI (the script is
    // refused by the request interception, and by the Content-Security-Policy when no key is configured).
    await page.route("**/api/config", (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ catalogApiUrl: "http://localhost:8080", catalogApiSource: "default", googleMapsApiKey: "AIzaSyTestKeyForPlaywright0123456789" }),
      }),
    );
    await page.route(/^https:\/\/maps\.googleapis\.com\//, (route) => route.abort());
    await openMap(page);
    const main = page.getByRole("main");
    await expect(main.getByText("Nie udało się wczytać mapy Google. Wyniki są pokazane jako lista.")).toBeVisible();
    await expect(main.locator("li.map-list__item").first()).toBeVisible();
    await expect(main.getByRole("button", { name: "Mapa", exact: true })).toHaveCount(0);
  });
});

test.describe("map markers BFF route", () => {
  test("rejects a missing or inverted area with a Polish message", async ({ request }) => {
    for (const query of ["", "south=55&west=14&north=48&east=24", "south=48&west=24&north=55&east=14", "south=a&west=1&north=2&east=3"]) {
      const response = await request.get(`/api/map/markers?${query}`);
      expect(response.status(), query).toBe(400);
      const body = (await response.json()) as { error: { code: string; message: string } };
      expect(body.error.code).toBe("validation_failed");
      expect(body.error.message).toBe("Nieprawidłowy obszar mapy.");
    }
  });

  test("rejects out-of-range coordinates and malformed filters", async ({ request }) => {
    expect((await request.get("/api/map/markers?south=-91&west=14&north=55&east=24")).status()).toBe(400);
    expect((await request.get(`/api/map/markers?${BBOX}&gmina=12`)).status()).toBe(400);
    expect((await request.get(`/api/map/markers?${BBOX}&kind=best`)).status()).toBe(400);
    expect((await request.get(`/api/map/markers?${BBOX}&maxTuition=99999999`)).status()).toBe(400);
    expect((await request.get(`/api/map/markers?${BBOX}&publicOnly=maybe`)).status()).toBe(400);
  });

  test("answers with the contract fields only and no score, rank or review fields", async ({ request }) => {
    const response = await request.get(`/api/map/markers?${BBOX}&sort=score&orderBy=rank`);
    expect(response.status()).toBe(200);
    expect(response.headers()["cache-control"]).toContain("max-age=60");
    const body = (await response.json()) as { items: Record<string, unknown>[]; total: number; truncated: boolean; limit: number };
    expect(Object.keys(body).sort()).toEqual(["items", "limit", "total", "truncated"]);
    expect(body.items.length).toBeGreaterThan(0);
    const allowed = ["freePlacesCount", "gminaName", "isPublic", "kind", "latitude", "longitude", "monthlyTuition", "name", "slug"];
    for (const item of body.items) expect(Object.keys(item).sort()).toEqual(allowed);
  });

  test("filters narrow the answer (kind, name)", async ({ request }) => {
    const schools = (await (await request.get(`/api/map/markers?${BBOX}&kind=school`)).json()) as { items: { kind: string }[] };
    expect(schools.items.length).toBeGreaterThan(0);
    expect(schools.items.every((item) => item.kind === "school")).toBe(true);
    const none = (await (await request.get(`/api/map/markers?${BBOX}&q=zzzzzzzzqq`)).json()) as { total: number };
    expect(none.total).toBe(0);
  });

  test("the runtime config exposes a Google Maps key only when one is configured", async ({ request }) => {
    const body = (await (await request.get("/api/config")).json()) as Record<string, unknown>;
    expect(typeof body.catalogApiUrl).toBe("string");
    if ("googleMapsApiKey" in body) expect(body.googleMapsApiKey).toMatch(/^[A-Za-z0-9_-]{20,128}$/);
  });
});

test.describe("map page: accessibility", () => {
  test("/mapa: no serious or critical axe violations", async ({ page }) => {
    await openMap(page);
    const results = await new AxeBuilder({ page }).withTags([...WCAG_TAGS]).analyze();
    const blocking = results.violations
      .filter((violation) => BLOCKING_IMPACTS.has(violation.impact ?? ""))
      .map((violation) => ({
        rule: violation.id,
        impact: violation.impact,
        nodes: violation.nodes.slice(0, 5).map((node) => node.target.join(" ")),
      }));
    expect(blocking, `blocking axe violations on /mapa: ${JSON.stringify(blocking, null, 2)}`).toEqual([]);
  });

  test("the nav links to the map page and marks it as current", async ({ page }) => {
    await openMap(page);
    const nav = page.getByRole("navigation", { name: "Główna" });
    const link = nav.getByRole("link", { name: "Mapa" });
    await expect(link).toHaveAttribute("href", "/mapa");
    await expect(link).toHaveAttribute("aria-current", "page");
  });
});

test.describe("map page: phone (360x740)", () => {
  test.use({ viewport: { width: 360, height: 740 }, isMobile: true, hasTouch: true });

  test("no sideways scroll and every control is at least 44px tall", async ({ page }) => {
    await openMap(page);
    const widths = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }));
    expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);

    const small = await page.evaluate(() =>
      Array.from(document.querySelectorAll<HTMLElement>("button, input:not([type=hidden]), select, textarea, nav a, footer a"))
        .filter((el) => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden")
        .filter((el) => !el.closest("[aria-hidden='true']"))
        .map((el) => {
          const isChoice = el instanceof HTMLInputElement && (el.type === "checkbox" || el.type === "radio");
          const box = (isChoice ? el.closest("label") ?? el : el).getBoundingClientRect();
          return { label: (el.getAttribute("aria-label") ?? el.textContent ?? "").trim().slice(0, 50), height: Math.round(box.height) };
        })
        .filter((target) => target.height < 44),
    );
    expect(small, `controls shorter than 44px: ${JSON.stringify(small)}`).toEqual([]);
  });

  test("the filters open as a bottom sheet, Escape closes it and returns focus to its button", async ({ page }) => {
    await openMap(page);
    const main = page.getByRole("main");
    const toggle = main.getByRole("button", { name: "Filtry" });
    await expect(main.getByRole("heading", { level: 2, name: "Opisz czego szukasz" })).toBeHidden();
    await toggle.click();
    const sheet = page.getByRole("dialog", { name: "Filtry mapy" });
    await expect(sheet).toBeVisible();
    await expect(sheet.getByRole("heading", { level: 2, name: "Opisz czego szukasz" })).toBeVisible();

    const widths = await page.evaluate(() => ({ s: document.documentElement.scrollWidth, c: document.documentElement.clientWidth }));
    expect(widths.s).toBeLessThanOrEqual(widths.c);

    await page.keyboard.press("Escape");
    await expect(sheet).toBeHidden();
    await expect(toggle).toBeFocused();
  });
});
