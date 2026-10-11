import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

// Acceptance journey against the synthetic demo gmina "Przykładowo" (TERYT 9999999), seeded by the API
// when SCHOOLSEARCH_DEMO_SEED=true (src/SchoolSearch.Api/Demo/DemoSeed.cs). Every value is fictional.
// Rules (architecture-standards E2E-ACCEPTANCE-TESTING §2): every test makes unconditional assertions;
// no guard-then-bail, no swallowed failures, no fixed sleeps. Waits are web-first (expect / expect.poll).

const DEMO_GMINA = "Przykładowo";
const DEMO_GMINA_NO_DATA = "Przykładowo-Wschód";
const DEMO_STREET = "Przykładowa";
const DEMO_NUMBER = "5";

const CATCHMENT_SCHOOL = "Szkoła Podstawowa nr 1 w Przykładowie (dane demonstracyjne)";
const EIGHT_SAT_SLUG = "demo-sp-3";
const COMPARE_SLUGS = ["demo-sp-1", "demo-sp-7", "demo-sp-8"];
const NO_DATA_SCHOOL = "Szkoła Podstawowa w Przykładowie-Wschód (dane demonstracyjne)";

/** Catalog API base URL as the web app is configured to call it (read from /api/config). */
async function catalogApi(request: APIRequestContext): Promise<string> {
  const response = await request.get("/api/config");
  expect(response.status()).toBe(200);
  const config = (await response.json()) as { catalogApiUrl?: unknown };
  expect(typeof config.catalogApiUrl).toBe("string");
  return config.catalogApiUrl as string;
}

/** Number text of a score cell, e.g. "62,4 ± 3,1" -> 62.4. Returns NaN when the cell has no number. */
function leadingNumber(text: string): number {
  const match = /-?\d+(?:[.,]\d+)?/.exec(text.trim());
  return match ? Number(match[0].replace(",", ".")) : Number.NaN;
}

/** Fills the catchment form on the home page and submits it. */
async function lookUpCatchment(page: Page, gmina: string, street: string, number: string): Promise<void> {
  await page.goto("/");
  await page.getByLabel("Gmina").selectOption({ label: gmina });
  await page.getByLabel("Ulica").fill(street);
  await page.getByLabel("Numer").fill(number);
  await page.getByRole("button", { name: "Sprawdź obwód" }).click();
}

test.describe("public journey on the synthetic demo gmina", () => {
  test.beforeAll(async ({ playwright }) => {
    // The API seeds the demo gmina after migrations, so poll the public gminas list before the first test.
    const request = await playwright.request.newContext({ baseURL: test.info().project.use.baseURL });
    try {
      const base = await catalogApi(request);
      await expect
        .poll(
          async () => {
            const response = await request.get(`${base}/api/v1/gminas`);
            if (!response.ok()) {
              return 0;
            }
            const gminas = (await response.json()) as { teryt: string; schoolCount: number }[];
            return gminas.find((g) => g.teryt === "9999999")?.schoolCount ?? 0;
          },
          { timeout: 90_000, intervals: [1_000, 2_000, 3_000] },
        )
        .toBeGreaterThan(0);
    } finally {
      await request.dispose();
    }
  });

  test("home: catchment school for a street and number shows score, interval, cohort, coverage and alternatives", async ({
    page,
    request,
  }) => {
    const base = await catalogApi(request);
    const methodology = (await (await request.get(`${base}/api/v1/methodology`)).json()) as { version: string };
    expect(methodology.version.length).toBeGreaterThan(0);

    await lookUpCatchment(page, DEMO_GMINA, DEMO_STREET, DEMO_NUMBER);

    const school = page.getByText(CATCHMENT_SCHOOL, { exact: true }).first();
    await expect(school).toBeVisible();
    await expect(page.getByText(/\d+(?:[.,]\d+)?\s*±\s*\d+(?:[.,]\d+)?/).first()).toBeVisible();
    await expect(page.getByText(/\d+\s+uczniów/).first()).toBeVisible();
    await expect(page.getByText(methodology.version, { exact: false }).first()).toBeVisible();
    await expect(page.getByText("street_list", { exact: true }).first()).toBeVisible();
    await expect(page.getByText(/Szkoły w zasięgu/).first()).toBeVisible();
  });

  test("profile: trend chart is an svg and the provenance table names the demo seed", async ({ page }) => {
    await page.goto("/szkoly/demo-sp-1");

    await expect(page.getByRole("heading", { level: 1 })).toContainText("Szkoła Podstawowa nr 1");
    const trend = page.locator("section", { has: page.getByRole("heading", { name: /trend/i }) });
    await expect(trend.locator("svg").first()).toBeVisible();

    const provenance = page.getByRole("table").filter({ hasText: "DEMO SEED" });
    await expect(provenance).toHaveCount(1);
    await expect(provenance.getByRole("row").filter({ hasText: "DEMO SEED" }).first()).toBeVisible();
  });

  test("8-sat school shows the small-group message and no numeric score", async ({ page }) => {
    await page.goto(`/szkoly/${EIGHT_SAT_SLUG}`);

    await expect(page.getByText("zbyt mała grupa", { exact: false }).first()).toBeVisible();
    await expect(page.getByText(/±/)).toHaveCount(0);
  });

  test("country ranking is non-increasing by score and offers no sort control", async ({ page }) => {
    await page.goto("/ranking/country");

    const table = page.getByRole("table").first();
    await expect(table).toBeVisible();

    const headers = await table.locator("thead th").allInnerTexts();
    const scoreColumn = headers.findIndex((header) => /wynik/i.test(header));
    expect(scoreColumn, `score column in headers: ${headers.join(" | ")}`).toBeGreaterThanOrEqual(0);

    const cells = table.locator("tbody tr").locator(`td:nth-child(${scoreColumn + 1})`);
    await expect(cells.first()).toBeVisible();
    const scores = (await cells.allInnerTexts()).map(leadingNumber).filter((n) => !Number.isNaN(n));
    expect(scores.length).toBeGreaterThan(1);
    for (let i = 1; i < scores.length; i += 1) {
      expect(scores[i], `score at row ${i + 1} must not exceed the row above`).toBeLessThanOrEqual(scores[i - 1]);
    }

    await expect(page.getByRole("combobox")).toHaveCount(0);
    await expect(page.locator("select")).toHaveCount(0);
    await expect(page.getByRole("button", { name: /sort/i })).toHaveCount(0);
    await expect(page.locator("thead button")).toHaveCount(0);
  });

  test("compare page shows three school columns with the same attribute rows", async ({ page }) => {
    await page.goto(`/porownaj?slugs=${COMPARE_SLUGS.join(",")}`);

    const table = page.getByRole("table").first();
    await expect(table).toBeVisible();

    const headerCells = table.locator("thead th");
    await expect(headerCells).toHaveCount(4);
    await expect(table.locator("thead th").nth(1)).toContainText("Szkoła Podstawowa nr 1");
    await expect(table.locator("thead th").nth(2)).toContainText("Szkoła Podstawowa nr 7");
    await expect(table.locator("thead th").nth(3)).toContainText("Szkoła Podstawowa nr 8");

    const rows = table.locator("tbody tr");
    await expect(rows.first()).toBeVisible();
    const rowCount = await rows.count();
    expect(rowCount).toBeGreaterThan(0);
    for (let i = 0; i < rowCount; i += 1) {
      // One attribute label plus one value per compared school: identical shape in every row.
      await expect(rows.nth(i).locator("th, td")).toHaveCount(4);
    }
  });

  test("correction form submit returns a public token", async ({ page }) => {
    await page.goto("/dla-szkol");

    await page.getByLabel("Nazwa szkoły").fill(CATCHMENT_SCHOOL);
    await page
      .getByLabel("Co jest błędne i jakie są właściwe dane?")
      .fill("Testowe zgłoszenie korekty (dane demonstracyjne).");
    await page.getByRole("button", { name: "Wyślij zgłoszenie" }).click();

    const status = page.getByRole("status");
    await expect(status).toContainText(/[A-Za-z0-9_-]{22,}/);
    await expect(status).toContainText(/token/i);
  });

  test("methodology page states that the portal is not an official ranking", async ({ page }) => {
    await page.goto("/metodologia");

    await expect(page.getByRole("heading", { level: 1, name: "Metodologia" })).toBeVisible();
    await expect(page.getByText(/nie jest oficjalnym rankingiem/i).first()).toBeVisible();
  });

  test("gmina without catchment data shows the no-data state and never a guessed school", async ({ page }) => {
    await lookUpCatchment(page, DEMO_GMINA_NO_DATA, DEMO_STREET, DEMO_NUMBER);

    await expect(page.getByText("brak danych", { exact: false }).first()).toBeVisible();
    await expect(page.getByText(NO_DATA_SCHOOL, { exact: true })).toHaveCount(0);
    await expect(page.getByText(CATCHMENT_SCHOOL, { exact: true })).toHaveCount(0);
    await expect(page.getByText(/±/)).toHaveCount(0);
  });
});
