import { expect, test } from "@playwright/test";

// Limited-confidence band (ADR-0009): a cohort of 5 to 8 pupils who sat the exam shows descriptive data and the
// cohort size, never a score, an interval or a rank. The demo school demo-sp-3 has 8 pupils who sat in both demo
// exam years (src/SchoolSearch.Api/Demo/DemoSeed.cs). Every value is fictional.
// Rules (architecture-standards E2E-ACCEPTANCE-TESTING §2): unconditional assertions, web-first waits, no sleeps.

const LIMITED_SLUG = "demo-sp-3";
const LIMITED_NAME = "nr 3 w Przykładowie";
const PUBLISHED_CONTROL_NAME = "nr 1 w Przykładowie";
const LIMITED_LINE = "Dla tej grupy nie publikujemy wyniku ani miejsca w zestawieniu.";

test.describe("limited-confidence band, 8 pupils (ADR-0009)", () => {
  test("profile shows the limited block with cohort size and the explicit line, and no value or interval", async ({ page }) => {
    await page.goto(`/szkoly/${LIMITED_SLUG}`);

    const block = page.locator('[data-status="limited"]');
    await expect(block).toHaveCount(1);
    await expect(block).toBeVisible();
    await expect(block.getByText(LIMITED_LINE, { exact: true })).toBeVisible();
    await expect(block.getByText("Kohorta: 8 uczniów", { exact: true })).toBeVisible();

    // No number, no interval and no "±" anywhere on the page for this school.
    await expect(block.locator(".score__number")).toHaveCount(0);
    await expect(page.locator(".score__number")).toHaveCount(0);
    await expect(page.getByText(/±/)).toHaveCount(0);
    await expect(page.getByText(/przedział 95%/)).toHaveCount(0);
  });

  test("profile offers no AI explanation: the explain panel is replaced by the published-only note", async ({ page }) => {
    await page.goto(`/szkoly/${LIMITED_SLUG}`);

    await expect(page.getByText("Wyjaśnienie AI jest dostępne, gdy szkoła ma opublikowany wynik.", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /wyjaśnij/i })).toHaveCount(0);
  });

  test("the country ranking lists the published control school and never the limited school", async ({ page }) => {
    await page.goto("/ranking/country");

    // Positive control: the page renders a published school, so the absence below is a real absence.
    await expect(page.getByText(PUBLISHED_CONTROL_NAME, { exact: false }).first()).toBeVisible();
    await expect(page.getByText(LIMITED_NAME, { exact: false })).toHaveCount(0);
  });

  test("the gmina ranking lists the published control school and never the limited school", async ({ page }) => {
    await page.goto("/ranking/gmina:9999999");

    await expect(page.getByText(PUBLISHED_CONTROL_NAME, { exact: false }).first()).toBeVisible();
    await expect(page.getByText(LIMITED_NAME, { exact: false })).toHaveCount(0);
  });

  test("compare page renders the limited block beside a published school, with no score for it", async ({ page }) => {
    await page.goto(`/porownaj?slugs=${LIMITED_SLUG},demo-sp-1`);

    await expect(page.locator('[data-status="limited"]')).toHaveCount(1);
    await expect(page.locator('[data-status="published"]')).toHaveCount(1);
    await expect(page.getByText(LIMITED_LINE, { exact: true })).toBeVisible();
  });
});
