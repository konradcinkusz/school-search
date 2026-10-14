import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { intentToSearchFilters, parseSearchIntent } from "../../../web/app/src/lib/ai-contract";
import { listTarget } from "../../../web/app/src/components/filters/helpers";
import {
  EMPTY_FILTERS,
  describeFilters,
  filtersFromSearchParams,
  filtersToSearchParams,
  removeFilter,
} from "../../../web/app/src/lib/search-filters";

// Shared filter model and AI filter bar (Phase M). The first block is pure (no page, no server). The second stubs the
// BFF search-intent call with page.route, so no model is involved and the result is deterministic.

const INTENT_URL = "**/api/proxy/api/v1/ai/search-intent";
const AI_LABEL = "Wygenerowano przez AI — to nie jest ocena szkoły";

test.describe("search-filters: pure functions", () => {
  test("empty filters produce no parameters; kind all is omitted", () => {
    expect(filtersToSearchParams(EMPTY_FILTERS).toString()).toBe("");
    expect(filtersToSearchParams({ kind: "all", q: "  " }).toString()).toBe("");
  });

  test("round trip keeps every valid field", () => {
    const filters = {
      kind: "preschool" as const,
      q: "Słoneczko",
      gmina: "9999999",
      publicOnly: true,
      maxTuitionPln: 1500,
      hasFreePlaces: true,
      place: "Plac Wolności w Poznaniu",
      radiusKm: 2.5,
    };
    const params = filtersToSearchParams(filters);
    expect(params.get("maxTuition")).toBe("1500");
    expect(params.get("hasFreePlaces")).toBe("true");
    expect(filtersFromSearchParams(new URLSearchParams(params.toString()))).toEqual(filters);
  });

  test("reader ignores junk and out-of-range values", () => {
    const read = filtersFromSearchParams({
      kind: "żłobek",
      gmina: "12",
      maxTuition: "99999",
      radiusKm: "500",
      publicOnly: "maybe",
      hasFreePlaces: ["true", "false"],
      place: "52.1, 21.0",
      q: ["abc"],
      foo: "bar",
    });
    expect(read).toEqual({ kind: "all", hasFreePlaces: true, q: "abc" });
  });

  test("place rejects e-mail, long digit runs and coordinate pairs", () => {
    for (const place of ["jan@example.com", "tel 600 700 800", "lat 52.1, 21.0", "52.1 21.0"]) {
      expect(filtersFromSearchParams({ place }).place, place).toBeUndefined();
    }
    expect(filtersFromSearchParams({ place: "ul. Długa 12, Gdańsk" }).place).toBe("ul. Długa 12, Gdańsk");
  });

  test("chips are Polish, ordered and removable", () => {
    const chips = describeFilters({ kind: "preschool", maxTuitionPln: 1500, hasFreePlaces: true, radiusKm: 2.5 });
    expect(chips.map((c) => c.label)).toEqual(["Rodzaj: przedszkole", "Czesne do 1500 zł", "Są wolne miejsca", "Do 2,5 km"]);
    expect(describeFilters(removeFilter({ kind: "preschool", maxTuitionPln: 1500 }, "kind"))).toEqual([
      { key: "maxTuitionPln", label: "Czesne do 1500 zł" },
    ]);
  });

  test("API mapping: maxDistanceKm becomes radiusKm, kind wins over type, absent new fields are unset", () => {
    const old = parseSearchIntent({ filters: { type: "preschool", maxDistanceKm: 3, publicOnly: true }, unsupported: [] });
    expect(old).not.toBeNull();
    expect(intentToSearchFilters(old!.filters)).toEqual({ kind: "preschool", publicOnly: true, radiusKm: 3 });

    const next = parseSearchIntent({
      filters: { type: "school", kind: "preschool", maxTuitionPln: 1500, hasFreePlaces: true, place: "Rynek", gmina: null, q: null },
      unsupported: ["Żłobki nie są jeszcze obsługiwane. Możesz szukać szkół i przedszkoli."],
    });
    expect(intentToSearchFilters(next!.filters)).toEqual({
      kind: "preschool",
      maxTuitionPln: 1500,
      hasFreePlaces: true,
      place: "Rynek",
    });
    expect(next!.unsupported).toHaveLength(1);
    // Strict: junk in the new fields is dropped.
    const junk = parseSearchIntent({ filters: { kind: "żłobek", maxTuitionPln: -5, hasFreePlaces: "yes", place: "a@b.pl" } });
    expect(intentToSearchFilters(junk!.filters)).toEqual({ kind: "all" });
  });

  test("list target routes by kind and reports map-only filters honestly", () => {
    expect(listTarget({ kind: "preschool", maxTuitionPln: 800, publicOnly: true }).href).toBe(
      "/przedszkola?publicOnly=true&maxTuition=800",
    );
    const school = listTarget({ kind: "school", q: "Kopernik", publicOnly: true });
    expect(school.href).toBe("/?q=Kopernik");
    expect(school.unapplied.join(" ")).toContain("publiczne");
    const map = listTarget({ kind: "preschool", place: "Rynek", radiusKm: 2 });
    expect(map.href).toBe("/przedszkola");
    expect(map.mapHref).toBe("/mapa?kind=preschool&place=Rynek&radiusKm=2");
    expect(map.unapplied.join(" ")).toContain("zastosujemy na mapie");
    expect(listTarget({ kind: "all", hasFreePlaces: true }).href).toBe("/przedszkola?hasFreePlaces=true");
  });
});

async function openHome(page: Page): Promise<void> {
  await page.goto("/");
  await page.waitForLoadState("networkidle");
}

async function ask(page: Page, text: string): Promise<void> {
  await page.getByRole("textbox", { name: "Opisz czego szukasz" }).fill(text);
  await page.getByRole("button", { name: "Przygotuj filtry" }).click();
}

const CANNED = {
  filters: {
    gmina: null,
    q: null,
    publicOnly: null,
    maxDistanceKm: 3,
    type: null,
    kind: "preschool",
    maxTuitionPln: 1500,
    hasFreePlaces: true,
    place: "Plac Wolności w Poznaniu",
    notes: [],
  },
  unsupported: ["Żłobki nie są jeszcze obsługiwane. Możesz szukać szkół i przedszkoli."],
  aiLabel: "Wygenerowano przez AI",
};

test.describe("AI filter bar on the home page (stubbed intent endpoint)", () => {
  test("result: label, chips, unsupported note, honest map hand-off, removal, manual edit and navigation", async ({ page }) => {
    await page.route(INTENT_URL, (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(CANNED) }));
    await openHome(page);
    await ask(page, "Przedszkole do 1500 zł z wolnymi miejscami blisko Placu Wolności w Poznaniu");

    const box = page.locator("section.ai-search");
    await expect(box.getByText(AI_LABEL, { exact: true })).toBeVisible();
    await expect(box.getByRole("note")).toContainText("Tego jeszcze nie obsługujemy:");
    await expect(box.getByRole("note")).toContainText("Żłobki nie są jeszcze obsługiwane");
    const chips = box.getByRole("list", { name: "Aktywne filtry" }).getByRole("listitem");
    await expect(chips).toHaveText([/Rodzaj: przedszkole/, /Czesne do 1500 zł/, /Są wolne miejsca/, /Okolica: Plac Wolności w Poznaniu/, /Do 3 km/]);
    await expect(page.getByRole("status")).toContainText("Przygotowano filtry: 5.");
    expect(new URL(page.url()).pathname).toBe("/");

    const results = page.getByRole("link", { name: "Pokaż wyniki z tymi filtrami" });
    await expect(results).toHaveAttribute("href", "/przedszkola?maxTuition=1500&hasFreePlaces=true");
    await expect(box.getByText(/zastosujemy na mapie/).first()).toBeVisible();
    await expect(box.getByRole("link", { name: "Zobacz te filtry na mapie" })).toHaveAttribute("href", /^\/mapa\?.*kind=preschool.*radiusKm=3/);

    // Remove a chip: the link follows, focus stays inside the chip group, the removal is announced.
    await box.getByRole("button", { name: "Usuń filtr: Czesne do 1500 zł" }).click();
    await expect(chips).toHaveCount(4);
    await expect(results).toHaveAttribute("href", "/przedszkola?hasFreePlaces=true");
    await expect(page.getByRole("status")).toContainText("Usunięto filtr: Czesne do 1500 zł.");
    await expect(box.locator(".filter-chips-wrap")).toBeFocused();

    // Manual edit (the edit panel opens after an AI result).
    const tuition = box.getByLabel("Czesne do (zł miesięcznie)");
    await tuition.fill("800");
    await expect(chips.filter({ hasText: "Czesne do 800 zł" })).toHaveCount(1);
    await expect(results).toHaveAttribute("href", "/przedszkola?maxTuition=800&hasFreePlaces=true");

    // Invalid input: described, aria-invalid, filters unchanged.
    const gmina = box.getByLabel("Kod gminy (7 cyfr)");
    await gmina.fill("12");
    await expect(gmina).toHaveAttribute("aria-invalid", "true");
    await expect(box.getByText("Kod gminy ma siedem cyfr, bez spacji.")).toBeVisible();
    await expect(results).toHaveAttribute("href", "/przedszkola?maxTuition=800&hasFreePlaces=true");
    await gmina.fill("9999999");
    await expect(gmina).not.toHaveAttribute("aria-invalid", "true");
    await expect(results).toHaveAttribute("href", "/przedszkola?gmina=9999999&maxTuition=800&hasFreePlaces=true");

    await results.click();
    await expect(page).toHaveURL(/\/przedszkola\?gmina=9999999&maxTuition=800&hasFreePlaces=true$/);
  });

  test("manual filters work without any AI call", async ({ page }) => {
    const sent: string[] = [];
    page.on("request", (request) => {
      if (request.url().includes("/ai/search-intent")) sent.push(request.url());
    });
    await openHome(page);
    await page.getByRole("button", { name: "Ustaw filtry ręcznie" }).click();
    await page.locator("section.ai-search").getByLabel("Nazwa lub słowo kluczowe").fill("Kopernik");
    await expect(page.getByRole("link", { name: "Pokaż wyniki z tymi filtrami" })).toHaveAttribute("href", "/?q=Kopernik");
    expect(sent).toHaveLength(0);
  });

  test("503 shows the degraded text, no chips, no AI label and no results link", async ({ page }) => {
    await page.route(INTENT_URL, (route) =>
      route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ error: { code: "ai_unavailable", message: "x" } }) }),
    );
    await openHome(page);
    await ask(page, "Przedszkole z wolnymi miejscami");

    await expect(page.getByRole("alert").filter({ hasText: "Asystent AI jest teraz niedostępny" })).toBeVisible();
    const box = page.locator("section.ai-search");
    await expect(box.getByText(AI_LABEL)).toHaveCount(0);
    await expect(box.locator(".filter-chip")).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Pokaż wyniki z tymi filtrami" })).toHaveCount(0);
    // Manual filters still work.
    await expect(page.getByRole("button", { name: "Ustaw filtry ręcznie" })).toBeVisible();
  });

  test("an old API answer without the new fields still yields chips", async ({ page }) => {
    await page.route(INTENT_URL, (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ filters: { type: "preschool", publicOnly: true }, unsupported: [] }),
      }),
    );
    await openHome(page);
    await ask(page, "publiczne przedszkole");
    await expect(page.locator("section.ai-search .filter-chip")).toHaveText([/Rodzaj: przedszkole/, /Tylko publiczne/]);
  });

  test("at 360 px the open bar with chips and manual fields does not overflow", async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await page.route(INTENT_URL, (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(CANNED) }));
    await openHome(page);
    await ask(page, "Przedszkole do 1500 zł z wolnymi miejscami blisko Placu Wolności w Poznaniu");
    await expect(page.locator("section.ai-search .filter-chip")).toHaveCount(5);

    const widths = await page.evaluate(() => ({
      scroll: document.documentElement.scrollWidth,
      client: document.documentElement.clientWidth,
    }));
    expect(widths.scroll).toBeLessThanOrEqual(widths.client);

    const small = await page.locator("section.ai-search").locator("button, input, select, textarea, a").evaluateAll((els) =>
      els
        .filter((el) => (el as HTMLElement).offsetParent !== null && (el as HTMLInputElement).type !== "checkbox")
        .map((el) => ({ tag: el.tagName, text: (el.textContent ?? "").slice(0, 30), h: el.getBoundingClientRect().height }))
        .filter((item) => item.h < 44),
    );
    expect(small).toEqual([]);
  });

  for (const scheme of ["light", "dark"] as const) {
    test(`axe finds no violations in the open bar (${scheme})`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await page.route(INTENT_URL, (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(CANNED) }));
      await openHome(page);
      await ask(page, "Przedszkole do 1500 zł z wolnymi miejscami blisko Placu Wolności w Poznaniu");
      await expect(page.locator("section.ai-search .filter-chip")).toHaveCount(5);
      const results = await new AxeBuilder({ page }).include("section.ai-search").analyze();
      expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`)).toEqual([]);
    });
  }
});
