import { expect, test, type Page } from "@playwright/test";

// Degraded services (C-06). A service that is down must show a Polish notice and a way back, never a blank page
// (P8). page.route forces 503 on the browser-side calls. Server-rendered calls, such as the catchment lookup on the
// home page, run inside the web server and cannot be intercepted from the browser; they are not forced here (see the
// gap note at the end of this file).
//
// CI state (ci.yml, web step): AGENT_BASE_URL is set, so /doradca renders its advisor. The advisor then answers the
// degraded state through the MCP service's 503 ai_unavailable. The "no agent configured" caution notice is therefore
// only checked when E2E_AGENT_UNSET=1, for a run whose web server has no AGENT_BASE_URL.

const UNAVAILABLE_ACCOUNT = /Konta są chwilowo niedostępne/;
const AI_DEGRADED = /Asystent AI jest teraz niedostępny/;
const RETURN_LINK = "Wróć do wyszukiwarki";

/** Answers every request to one URL pattern with the given status and JSON body. */
async function forceJson(page: Page, urlPattern: string, status: number, body: unknown): Promise<void> {
  await page.route(urlPattern, (route) =>
    route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) }),
  );
}

test.describe("account service down (/api/auth/session 503)", () => {
  test("/konto shows the unavailable notice with a link back to the search", async ({ page }) => {
    await forceJson(page, "**/api/auth/session", 503, {
      error: { code: "auth_unavailable", message: "Konta są chwilowo niedostępne." },
    });
    await page.goto("/konto");
    await page.waitForLoadState("networkidle");

    const notice = page.getByRole("status").filter({ hasText: UNAVAILABLE_ACCOUNT });
    await expect(notice).toBeVisible();
    const back = notice.getByRole("link", { name: RETURN_LINK });
    await expect(back).toHaveAttribute("href", "/");
    await back.click();
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  });

  test("/konto/szkola gates the page with the same notice and a link back to the search", async ({ page }) => {
    await forceJson(page, "**/api/auth/session", 503, {
      error: { code: "auth_unavailable", message: "Konta są chwilowo niedostępne." },
    });
    await page.goto("/konto/szkola");
    await page.waitForLoadState("networkidle");

    const notice = page.getByRole("status").filter({ hasText: UNAVAILABLE_ACCOUNT });
    await expect(notice).toBeVisible();
    await expect(notice.getByRole("link", { name: RETURN_LINK })).toHaveAttribute("href", "/");
  });
});

test.describe("advisor service down (/api/agent 503)", () => {
  test("a forced agent outage shows the AI degrade state and keeps the visitor's message", async ({ page }) => {
    await forceJson(page, "**/api/agent", 503, {
      error: { code: "agent_unavailable", message: "Doradca AI jest chwilowo niedostępny." },
    });
    await page.goto("/doradca");
    await page.waitForLoadState("networkidle");

    await page.getByLabel("Twoja wiadomość").fill("Szukam szkoły blisko domu.");
    await page.getByRole("button", { name: "Wyślij" }).click();

    await expect(page.getByRole("alert").filter({ hasText: AI_DEGRADED })).toBeVisible();
    await expect(page.getByLabel("Twoja wiadomość")).toHaveValue("Szukam szkoły blisko domu.");
    await expect(page.locator(".ai-msg")).toHaveCount(0);
  });

  test("with an agent configured, /doradca shows no caution notice (true for CI)", async ({ page }) => {
    await page.goto("/doradca");
    await page.waitForLoadState("networkidle");
    await expect(page.getByRole("heading", { level: 1, name: "Doradca wyboru szkoły" })).toBeVisible();
    await expect(page.getByText("Doradca AI jest chwilowo niedostępny.")).toHaveCount(0);
  });

  test("with no agent configured, /doradca shows the caution notice and a link back to the search", async ({ page }) => {
    test.skip(process.env.E2E_AGENT_UNSET !== "1", "needs a web server started without AGENT_BASE_URL (E2E_AGENT_UNSET=1)");
    await page.goto("/doradca");
    await page.waitForLoadState("networkidle");

    const notice = page.locator(".notice--caution").filter({ hasText: "Doradca AI jest chwilowo niedostępny." });
    await expect(notice).toBeVisible();
    await expect(notice.getByRole("link", { name: RETURN_LINK })).toHaveAttribute("href", "/");
    await expect(notice.getByRole("link", { name: "Przeczytaj metodologię" })).toHaveAttribute("href", "/metodologia");
  });
});

// Gap: the catchment call (CatchmentPanel on the home page, fetched by the web server) cannot be forced with page.route.
// Its "Usługa jest chwilowo niedostępna" notice has the same shape as ServiceUnavailable, which is covered above through
// the account page. A test for the catchment path needs the web server to reach a stub catalog, not a browser route.
