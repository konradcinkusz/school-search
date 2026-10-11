import { expect, test } from "@playwright/test";

// Monetisation surface (SPEC-F, F4): pricing, "what is not sold", the sponsored block, owner area and moderation tab.
// The Billing service is not started by this suite by default: each assertion accepts the configured state and the
// degraded state (BILLING_BASE_URL unset, accounts unset) explicitly. The real billing flow against a live authservice
// is NOT RUN here (documented in docs/runbooks/BILLING.md). Sponsored content must never appear in a list or ranking.

const DEMO_SCHOOL = "demo-sp-1";
const SPONSORED = "Treść sponsorowana";
const NOT_SOLD_HEADING = "Czego nie sprzedajemy";

test.describe("monetisation surface", () => {
  test("pricing page renders the fixed statements and one of the plan states, never a blank page", async ({ page }) => {
    await page.goto("/cennik");

    await expect(page.getByRole("heading", { level: 1, name: "Cennik" })).toBeVisible();
    await expect(page.getByText("Czego nie sprzedajemy:")).toBeVisible();
    await expect(
      page.getByText("Płatny profil nie wpływa na wynik, ranking, kolejność szkoły ani na opinie użytkowników."),
    ).toBeVisible();
    await expect(
      page
        .getByText("Cennik w przygotowaniu.")
        .or(page.getByText("Płatne profile są chwilowo niedostępne."))
        .or(page.locator(".pricing")),
    ).toBeVisible();
  });

  test("pricing page never invents prices or legal wording while the terms are in review", async ({ page }) => {
    await page.goto("/cennik");

    await expect(page.getByText("Warunki sprzedaży są w przygotowaniu i wymagają weryfikacji prawnej.")).toBeVisible();
    await expect(page.getByText(/VAT|UOKiK|odstąpienia od umowy/)).toHaveCount(0);
  });

  test("for-schools page states what is not sold and how a claim works", async ({ page }) => {
    await page.goto("/dla-szkol");

    await expect(page.getByRole("heading", { level: 1, name: "Dla szkół" })).toBeVisible();
    await expect(page.getByText("Pozycja szkoły nie jest do kupienia.")).toBeVisible();
    await expect(page.getByRole("heading", { level: 3, name: NOT_SOLD_HEADING })).toBeVisible();
    await expect(page.getByText("Potwierdzenie przedstawiciela przez redakcję nie jest do kupienia.")).toBeVisible();
  });

  test("school page shows no sponsored card when the school has no published profile", async ({ page }) => {
    await page.goto(`/szkoly/${DEMO_SCHOOL}`);

    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Profil rozszerzony szkoły" })).toHaveCount(0);
    await expect(page.getByText(SPONSORED)).toHaveCount(0);
  });

  test("sponsored content never appears in rankings, comparisons or the school list", async ({ page }) => {
    for (const path of ["/ranking/country", `/porownaj?s=${DEMO_SCHOOL}`, "/przedszkola"]) {
      await page.goto(path);
      await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
      await expect(page.getByText(SPONSORED)).toHaveCount(0);
    }
  });

  test("owner area asks for a login or shows the unavailable state, never the owner tools", async ({ page }) => {
    await page.goto("/konto/szkola");

    await expect(page.getByRole("heading", { level: 1, name: "Konto szkoły" })).toBeVisible();
    await expect(
      page
        .getByText("Zaloguj się, aby zarządzać profilem szkoły", { exact: false })
        .or(page.getByText("Konta są chwilowo niedostępne.", { exact: false })),
    ).toBeVisible();
    await expect(page.getByRole("button", { name: "Wyślij zgłoszenie" })).toHaveCount(0);
  });

  test("an invalid school slug in the owner area is not found", async ({ request }) => {
    const response = await request.get("/konto/szkola/Not_A_Slug!");
    expect(response.status()).toBe(404);
  });

  test("moderation has a school claims tab next to the reviews queue", async ({ page }) => {
    await page.goto("/moderacja");

    await expect(page.getByRole("tab", { name: "Opinie i zgłoszenia" })).toHaveAttribute("aria-selected", "true");
    await page.getByRole("tab", { name: "Zgłoszenia właścicieli" }).click();
    await expect(page.getByRole("tab", { name: "Zgłoszenia właścicieli" })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByLabel("Status zgłoszeń")).toBeVisible();
  });

  test("the proxy never serves a billing write to a visitor without a session", async ({ request, baseURL }) => {
    const origin = baseURL ?? "http://localhost:3000";
    const premium = await request.put(`/api/proxy/api/v1/schools/${DEMO_SCHOOL}/premium`, {
      headers: { origin, "content-type": "application/json" },
      data: { headline: "Test", description: "Test", websiteUrl: null },
    });
    // 401 when Billing is configured and no session exists; 503 when Billing is not configured. Never 200.
    expect([401, 503]).toContain(premium.status());

    const simulate = await request.post("/api/proxy/api/v1/billing/simulate", {
      headers: { origin, "content-type": "application/json" },
      data: { subscriptionId: "00000000-0000-0000-0000-000000000000", event: "payment_succeeded" },
    });
    expect([401, 503]).toContain(simulate.status());
  });

  test("the payment webhook is not reachable from the browser", async ({ request, baseURL }) => {
    const response = await request.post("/api/proxy/api/v1/billing/webhook", {
      headers: { origin: baseURL ?? "http://localhost:3000", "content-type": "application/json" },
      data: {},
    });
    expect(response.status()).toBe(404);
  });

  test("a parent enquiry is never accepted for a school without a published profile", async ({ request, baseURL }) => {
    const response = await request.post(`/api/proxy/api/v1/schools/${DEMO_SCHOOL}/enquiries`, {
      headers: { origin: baseURL ?? "http://localhost:3000", "content-type": "application/json" },
      data: {
        email: "rodzic@example.invalid",
        message: "Testowa wiadomość (dane demonstracyjne).",
        acceptedConsentVersion: "enquiry-consent-draft-1",
        website: "",
      },
    });
    // 404 when Billing is configured and the school has no profile; 503 when Billing is not configured. Never 202.
    expect([404, 503]).toContain(response.status());
  });
});
