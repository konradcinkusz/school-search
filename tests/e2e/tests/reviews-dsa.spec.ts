import { expect, test } from "@playwright/test";

// Accounts, reviews and DSA pages (SPEC-E §E3). Demo school "demo-sp-1" is seeded by the API (see journey.spec.ts).
// Reviews are user-generated and never shown as a score. Login and registration against a real authservice are NOT
// run in CI: the suite checks only the degraded or form states that do not need an identity provider.
// Assertions are unconditional. Where a route answers 401 or 503 depending on configuration, both are asserted
// explicitly rather than skipped.

const DEMO_SCHOOL = "demo-sp-1";
const REVIEWS_NOTICE_START = "Opinie pochodzą od zalogowanych użytkowników z potwierdzonym adresem e-mail";

test.describe("reviews and DSA surface", () => {
  test("school profile shows a separate reviews section with the fixed notice and an empty state", async ({ page }) => {
    await page.goto(`/szkoly/${DEMO_SCHOOL}`);

    const section = page.locator("section", { has: page.getByRole("heading", { name: "Opinie użytkowników" }) });
    await expect(section).toBeVisible();
    await expect(section.getByText(REVIEWS_NOTICE_START, { exact: false })).toBeVisible();
    await expect(section.getByText("Nie ma jeszcze opinii dla tej szkoły.")).toBeVisible();
    // Reviews never feed a score: the section shows no average and no aggregate rating.
    await expect(section.getByText(/średni/i)).toHaveCount(0);
  });

  test("writing a review requires login: the form is not shown to a visitor", async ({ page }) => {
    await page.goto(`/szkoly/${DEMO_SCHOOL}`);

    const section = page.locator("section", { has: page.getByRole("heading", { name: "Opinie użytkowników" }) });
    // Either the login gate (accounts configured) or the unavailable note (accounts not configured).
    await expect(
      section
        .getByText("Aby napisać opinię, zaloguj się")
        .or(section.getByText("Konta są chwilowo niedostępne, więc nie można teraz dodać opinii.")),
    ).toBeVisible();
    await expect(section.getByRole("button", { name: "Napisz opinię" })).toHaveCount(0);
    await expect(section.getByRole("textbox", { name: /Treść opinii/ })).toHaveCount(0);
  });

  test("DSA pages render: notice form, terms draft, privacy notice and contact point", async ({ page }) => {
    await page.goto("/zglos-tresc");
    await expect(page.getByRole("heading", { level: 1, name: "Zgłoś treść" })).toBeVisible();
    await expect(page.getByLabel("Potwierdzam, że zgłoszenie składam w dobrej wierze", { exact: false })).toBeVisible();

    await page.goto("/regulamin");
    await expect(page.getByRole("heading", { level: 1, name: "Regulamin" })).toBeVisible();
    await expect(page.getByText("Projekt do weryfikacji przez prawnika.", { exact: false })).toBeVisible();

    await page.goto("/prywatnosc");
    await expect(page.getByRole("heading", { level: 1, name: "Polityka prywatności" })).toBeVisible();

    await page.goto("/kontakt");
    await expect(page.getByRole("heading", { level: 1, name: "Kontakt" })).toBeVisible();
  });

  test("notice form submit returns an acknowledgement token", async ({ page }) => {
    await page.goto(`/zglos-tresc?slug=${DEMO_SCHOOL}`);

    await expect(page.getByLabel("Adres treści lub jej identyfikator")).toHaveValue(`/szkoly/${DEMO_SCHOOL}`);
    await page
      .getByLabel("Dlaczego treść jest niezgodna z prawem lub regulaminem?")
      .fill("Testowe zgłoszenie treści (dane demonstracyjne) do sprawdzenia przez moderatora.");
    await page.getByLabel("Potwierdzam, że zgłoszenie składam w dobrej wierze", { exact: false }).check();
    await page.getByRole("button", { name: "Wyślij zgłoszenie" }).click();

    const status = page.getByRole("status");
    await expect(status).toContainText("Zgłoszenie przyjęte.");
    await expect(status).toContainText("Token potwierdzenia:");
    await expect(status.locator(".token")).toHaveCount(2);
    await expect(status.locator(".token").nth(1)).toHaveText(/[A-Za-z0-9_-]{16,}/);
  });

  test("login page shows the unavailable state or the sign-in form, never a blank page", async ({ page }) => {
    await page.goto("/konto/logowanie");

    await expect(page.getByRole("heading", { level: 1, name: "Logowanie" })).toBeVisible();
    await expect(
      page.getByText("Konta są chwilowo niedostępne.").or(page.getByLabel("Adres e-mail")),
    ).toBeVisible();
  });

  test("session endpoint never reveals tokens: 200 with two booleans, or 503 when accounts are not configured", async ({
    request,
  }) => {
    const response = await request.get("/api/auth/session");
    expect([200, 503]).toContain(response.status());
    const text = await response.text();
    expect(text).not.toMatch(/accessToken|refreshToken|ss_session/);
    if (response.status() === 200) {
      const body = JSON.parse(text) as Record<string, unknown>;
      expect(Object.keys(body).sort()).toEqual(["authenticated", "emailVerified"]);
      expect(body.authenticated).toBe(false);
    }
  });

  test("the proxy refuses DELETE outside the own-review allowlist", async ({ request, baseURL }) => {
    const response = await request.delete(`/api/proxy/api/v1/schools/${DEMO_SCHOOL}`, {
      headers: { origin: baseURL ?? "http://localhost:3000" },
    });
    expect(response.status()).toBe(405);
  });

  test("a review write without a session is never accepted anonymously", async ({ request, baseURL }) => {
    const response = await request.post(`/api/proxy/api/v1/schools/${DEMO_SCHOOL}/reviews`, {
      headers: { origin: baseURL ?? "http://localhost:3000", "content-type": "application/json" },
      data: { rating: 5, title: "Test", body: "x".repeat(60), schoolYearFrom: 2025, acceptedTermsVersion: "v" },
    });
    // 401 when the Reviews service is configured and no session exists; 503 when Reviews is not configured.
    expect([401, 503]).toContain(response.status());
    const body = (await response.json()) as { error?: { code?: string } };
    expect(typeof body.error?.code).toBe("string");
  });

  test("moderation queue is not served to a visitor without a session", async ({ request }) => {
    const response = await request.get("/api/proxy/api/v1/moderation/queue");
    // 401 when Reviews is configured; 503 when it is not. Never 200.
    expect([401, 503]).toContain(response.status());
  });
});
