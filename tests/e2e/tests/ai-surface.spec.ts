import { expect, test, type Page } from "@playwright/test";

// AI surface (SPEC-G, G7). CI runs without ANTHROPIC_API_KEY and without AGENT_BASE_URL, so every AI box must show its
// degrade state (P8) while the rest of the page keeps working. Assertions are unconditional: the degraded copy is
// asserted explicitly, never skipped. Nothing here calls a live model.

const AI_DEGRADED = /Asystent AI jest teraz niedostępny/;
const NOT_RANKING = /najlepsz|najgorsz/i;

/**
 * Opens a page and waits until its client bundle has loaded. The AI boxes are client components: a click made before
 * hydration would hit the server-rendered form, so the wait is part of the page open, not a fixed sleep.
 */
async function openPage(page: Page, path: string): Promise<void> {
  await page.goto(path);
  await page.waitForLoadState("networkidle");
}

test.describe("AI surface: degrade states and guard rails", () => {
  test("search box: empty input is refused locally and sends no request", async ({ page }) => {
    const sent: string[] = [];
    page.on("request", (request) => {
      if (request.url().includes("/ai/search-intent")) sent.push(request.url());
    });

    await openPage(page, "/");
    await expect(page.getByRole("heading", { level: 2, name: "Opisz czego szukasz" })).toBeVisible();
    await page.getByRole("button", { name: "Przygotuj filtry" }).click();

    await expect(page.getByRole("alert").filter({ hasText: "Napisz, czego szukasz." })).toBeVisible();
    expect(sent).toHaveLength(0);
  });

  test("search box: with no key the AI degrade state shows and the page never navigates", async ({ page }) => {
    await openPage(page, "/");
    const before = new URL(page.url());

    await page.getByRole("textbox", { name: "Opisz czego szukasz" }).fill("Szkoła podstawowa w gminie Przykładowo, z oddziałem sportowym");
    await page.getByRole("button", { name: "Przygotuj filtry" }).click();

    await expect(page.getByRole("alert").filter({ hasText: AI_DEGRADED })).toBeVisible();
    // Nothing auto-navigates to results or a ranking: the URL is unchanged and no filter chips appear.
    const after = new URL(page.url());
    expect(after.pathname).toBe(before.pathname);
    expect(after.search).toBe("");
    await expect(page.getByRole("link", { name: "Pokaż wyniki z tymi filtrami" })).toHaveCount(0);
  });

  test("search box: the AI result never reaches the school list by itself", async ({ page }) => {
    await openPage(page, "/");
    await page.getByRole("textbox", { name: "Opisz czego szukasz" }).fill("najlepsza szkoła w okolicy");
    await page.getByRole("button", { name: "Przygotuj filtry" }).click();

    await expect(page.getByRole("alert").filter({ hasText: AI_DEGRADED })).toBeVisible();
    expect(new URL(page.url()).pathname).toBe("/");
    const aiBox = page.locator("section.ai-search");
    await expect(aiBox).toBeVisible();
    // The visitor's own words sit in the textarea and are not portal copy: only rendered text is checked.
    await expect(aiBox.locator("p, li, label, legend, h2").filter({ hasText: NOT_RANKING })).toHaveCount(0);
  });

  test("methodology: the question box shows the degrade state and the methodology text stays intact", async ({ page }) => {
    await openPage(page, "/metodologia");

    await expect(page.getByRole("heading", { level: 1, name: "Metodologia" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Zapytaj o metodologię" })).toBeVisible();
    await page.getByLabel("Twoje pytanie o metodologię").fill("Jak liczony jest przedział niepewności?");
    await page.getByRole("button", { name: "Zapytaj" }).click();

    await expect(page.getByRole("alert").filter({ hasText: AI_DEGRADED })).toBeVisible();
    await expect(page.getByText(/nie jest oficjalnym rankingiem/i).first()).toBeVisible();
    await expect(page.getByText("Wygenerowano przez AI", { exact: false })).toHaveCount(0);
  });

  test("methodology: an empty question is refused locally", async ({ page }) => {
    await openPage(page, "/metodologia");
    await page.getByRole("button", { name: "Zapytaj" }).click();
    await expect(page.getByRole("alert").filter({ hasText: "Wpisz pytanie o metodologię." })).toBeVisible();
  });

  test("advisor: the page renders with the AI label, the disclaimer and the nav link", async ({ page }) => {
    await openPage(page, "/doradca");

    await expect(page.getByRole("heading", { level: 1, name: "Doradca wyboru szkoły" })).toBeVisible();
    await expect(page.getByText("Wygenerowano przez AI.", { exact: false })).toBeVisible();
    await expect(page.getByText(/Obwód szkolny ustala gmina\./)).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Główna" }).getByRole("link", { name: "Doradca" })).toBeVisible();
    await expect(page.locator("main, article").first().getByText(NOT_RANKING)).toHaveCount(0);
  });

  test("advisor: an empty message is refused locally and no request reaches the agent", async ({ page }) => {
    const sent: string[] = [];
    page.on("request", (request) => {
      if (request.url().includes("/api/agent")) sent.push(request.url());
    });

    await openPage(page, "/doradca");
    await page.getByRole("button", { name: "Wyślij" }).click();

    await expect(page.getByRole("alert").filter({ hasText: "Napisz, czego szukasz, zanim wyślesz wiadomość." })).toBeVisible();
    expect(sent).toHaveLength(0);
  });

  test("advisor: with no agent configured the degrade state shows and the message stays in the box", async ({ page }) => {
    await openPage(page, "/doradca");

    await page.getByLabel("Twoja wiadomość").fill("Szukam szkoły blisko domu.");
    await page.getByRole("button", { name: "Wyślij" }).click();

    await expect(page.getByRole("alert").filter({ hasText: AI_DEGRADED })).toBeVisible();
    await expect(page.getByLabel("Twoja wiadomość")).toHaveValue("Szukam szkoły blisko domu.");
    await expect(page.locator(".ai-msg")).toHaveCount(0);
  });

  test("advisor endpoint refuses a foreign or missing Origin", async ({ request }) => {
    const payload = { messages: [{ role: "user", content: "Test" }] };

    const foreign = await request.post("/api/agent", {
      headers: { origin: "https://evil.example", "content-type": "application/json" },
      data: payload,
    });
    expect(foreign.status()).toBe(403);

    const anonymous = await request.post("/api/agent", {
      headers: { "content-type": "application/json" },
      data: payload,
    });
    expect(anonymous.status()).toBe(403);
  });

  test("advisor endpoint refuses a malformed conversation before it reaches the MCP service", async ({ request, baseURL }) => {
    const origin = baseURL ?? "http://localhost:3000";
    const lastIsAssistant = await request.post("/api/agent", {
      headers: { origin, "content-type": "application/json" },
      data: { messages: [{ role: "assistant", content: "Witaj" }] },
    });
    expect(lastIsAssistant.status()).toBe(400);

    const tooLong = await request.post("/api/agent", {
      headers: { origin, "content-type": "application/json" },
      data: { messages: [{ role: "user", content: "x".repeat(1001) }] },
    });
    expect(tooLong.status()).toBe(400);
  });

  test("AI search endpoint through the proxy answers the degrade state, never 200 without a key", async ({ request, baseURL }) => {
    const response = await request.post("/api/proxy/api/v1/ai/search-intent", {
      headers: { origin: baseURL ?? "http://localhost:3000", "content-type": "application/json" },
      data: { question: "Szkoła w gminie Przykładowo" },
    });
    expect([503]).toContain(response.status());
    const body = (await response.json()) as { error?: { code?: string } };
    expect(typeof body.error?.code).toBe("string");
  });

  test("moderator corrections are not served to a visitor without a session", async ({ request, baseURL }) => {
    const origin = baseURL ?? "http://localhost:3000";
    const list = await request.get("/api/proxy/api/v1/admin/corrections");
    // 401 when accounts are configured; 503 when they are not. Never 200.
    expect([401, 503]).toContain(list.status());

    const triage = await request.post(
      "/api/proxy/api/v1/admin/corrections/00000000-0000-0000-0000-000000000000/triage",
      { headers: { origin } },
    );
    expect([401, 503]).toContain(triage.status());

    const foreignTriage = await request.post(
      "/api/proxy/api/v1/admin/corrections/00000000-0000-0000-0000-000000000000/triage",
      { headers: { origin: "https://evil.example" } },
    );
    expect(foreignTriage.status()).toBe(403);
  });

  test("moderation has corrections and complaints tabs next to the existing queues", async ({ page }) => {
    await openPage(page, "/moderacja");

    await page.getByRole("tab", { name: "Korekty" }).click();
    await expect(page.getByRole("tab", { name: "Korekty" })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByLabel("Status korekty")).toBeVisible();

    await page.getByRole("tab", { name: "Skargi" }).click();
    await expect(page.getByRole("tab", { name: "Skargi" })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByLabel("Identyfikator skargi")).toBeVisible();
  });

  test("correction form offers preschool fields only when the target is a preschool", async ({ page }) => {
    await openPage(page, "/dla-szkol");

    const fieldOptions = page.locator('select[name="field"] option');
    await expect(fieldOptions.filter({ hasText: "Czesne: minimum" })).toHaveCount(0);

    await page.getByLabel("Czego dotyczy zgłoszenie?").selectOption("preschool");
    await expect(page.getByLabel("Identyfikator przedszkola (slug)")).toBeVisible();
    await expect(fieldOptions.filter({ hasText: "Czesne: minimum" })).toHaveCount(1);
    await expect(fieldOptions.filter({ hasText: "Wolne miejsca" })).toHaveCount(1);
  });
});
