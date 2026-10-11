import { expect, test } from "@playwright/test";

test.describe("public journey", () => {
  test("home links to methodology, which states it is not an official ranking; config exposes the catalog URL", async ({
    page,
    request,
  }) => {
    await page.goto("/");

    const methodologyLink = page.getByRole("link", { name: "Metodologia", exact: true });
    await expect(methodologyLink).toBeVisible();
    await expect(methodologyLink).toHaveAttribute("href", "/metodologia");

    await methodologyLink.click();
    await expect(page).toHaveURL(/\/metodologia$/);
    await expect(page.getByRole("heading", { level: 1, name: "Metodologia" })).toBeVisible();
    await expect(page.getByText("Nie jest oficjalnym rankingiem", { exact: false }).first()).toBeVisible();

    const response = await request.get("/api/config");
    expect(response.status()).toBe(200);
    expect(response.headers()["content-type"]).toContain("application/json");
    const config = (await response.json()) as { catalogApiUrl?: unknown };
    expect(typeof config.catalogApiUrl).toBe("string");
    expect(config.catalogApiUrl as string).toMatch(/^https?:\/\//);
  });
});
