import { expect, test, type Page } from "@playwright/test";

// Local sign-in against a real authservice (Phase M). NOT part of CI: CI has no authservice, so the whole file is
// skipped unless E2E_AUTH=1. It needs the AppHost run with SeedDemo on (the seeded accounts and demo data) and the
// test password in E2E_TEST_PASSWORD; the password is never written in this repository.
//
//   E2E_AUTH=1 E2E_TEST_PASSWORD=<password> PLAYWRIGHT_BASE_URL=<web url from the Aspire dashboard> \
//     npx playwright test auth-login
//
// Read-only except one thing: opening the owner's inbox marks the enquiries as read (that is the app's behaviour).
// The accounts, roles and demo rows are the ones documented in docs/runbooks/LOCAL-DEV.md sections 4 and 5.

const AUTH_ENABLED = process.env.E2E_AUTH === "1";
const PASSWORD = process.env.E2E_TEST_PASSWORD ?? "";

const PARENT = "parent@school-search.test";
const MODERATOR = "moderator@school-search.test";
const OWNER = "owner@school-search.test";
const OWNED_SCHOOL = "demo-sp-1";

async function signIn(page: Page, email: string, password: string): Promise<void> {
  await page.goto("/konto/logowanie");
  await expect(page.getByRole("heading", { level: 1, name: "Logowanie" })).toBeVisible();
  await page.getByLabel("Adres e-mail").fill(email);
  await page.getByLabel("Hasło").fill(password);
  await page.getByRole("button", { name: "Zaloguj się" }).click();
}

async function signInOk(page: Page, email: string): Promise<void> {
  await signIn(page, email, PASSWORD);
  await expect(page).toHaveURL(/\/konto$/);
  await expect(page.getByRole("heading", { level: 1, name: "Konto" })).toBeVisible();
}

test.describe("local sign-in with the seeded accounts", () => {
  test.describe.configure({ mode: "serial" });
  test.skip(!AUTH_ENABLED, "Needs a local authservice: run with E2E_AUTH=1 (never in CI).");
  test.skip(AUTH_ENABLED && PASSWORD === "", "Set E2E_TEST_PASSWORD to the seeded test password.");

  test("a failed login shows the Polish error and keeps the visitor signed out", async ({ page }) => {
    // A wrong password on purpose: this test must not need the real one.
    await signIn(page, PARENT, "Definitely-Not-The-Password-1!");

    await expect(page.getByRole("alert").filter({ hasText: "Nieprawidłowy adres e-mail lub hasło." })).toBeVisible();
    await expect(page).toHaveURL(/\/konto\/logowanie$/);

    await page.goto("/konto");
    await expect(page.getByText("Nie jesteś zalogowany.")).toBeVisible();
  });

  test("the login page offers the form, not the unavailable state (SESSION_SECRET and AUTH_BASE_URL are set)", async ({
    page,
  }) => {
    await page.goto("/konto/logowanie");

    await expect(page.getByText("Konta są chwilowo niedostępne.")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Zaloguj się" })).toBeVisible();
  });

  test("parent signs in, sees the account page with the seeded reviews and signs out", async ({ page, context }) => {
    await signInOk(page, PARENT);

    await expect(page.getByText("Jesteś zalogowany z potwierdzonym adresem e-mail.")).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Moje opinie" })).toBeVisible();
    // The parent has seeded reviews (published and pending): the list shows at least one status line.
    await expect(page.getByText(/^Status: /).first()).toBeVisible();

    const session = (await context.cookies()).find((cookie) => cookie.name === "ss_session");
    expect(session, "the sealed session cookie is set").toBeDefined();
    expect(session?.httpOnly).toBe(true);

    await page.getByRole("button", { name: "Wyloguj się" }).click();
    await expect(page).toHaveURL(/\/$/);

    await page.goto("/konto");
    await expect(page.getByText("Nie jesteś zalogowany.")).toBeVisible();
  });

  test("moderator opens the moderation console and sees the queue", async ({ page }) => {
    await signInOk(page, MODERATOR);

    await page.goto("/moderacja");
    await expect(page.getByRole("heading", { level: 1, name: "Moderacja" })).toBeVisible();
    await expect(page.getByRole("tablist", { name: "Kolejki moderacji" })).toBeVisible();
    await expect(page.getByRole("tab", { name: "Opinie i zgłoszenia" })).toBeVisible();

    await expect(page.getByText("Ładowanie kolejki…")).toHaveCount(0);
    await expect(page.getByText("Ta strona jest dostępna wyłącznie dla moderatorów.")).toHaveCount(0);
    await expect(page.getByText("Kolejka moderacji jest chwilowo niedostępna.")).toHaveCount(0);
    await expect(page.getByText("Zaloguj się, aby zobaczyć kolejkę.")).toHaveCount(0);
    // The seed leaves a pending review and an open notice for the moderator.
    await expect(page.locator("ul.plain-list > li").first()).toBeVisible();
  });

  test("a parent is told the moderation console is for moderators only", async ({ page }) => {
    await signInOk(page, PARENT);

    await page.goto("/moderacja");
    await expect(page.getByRole("alert").filter({ hasText: "Ta strona jest dostępna wyłącznie dla moderatorów." })).toBeVisible();
  });

  test("owner sees the approved school claim and the enquiries inbox", async ({ page }) => {
    await signInOk(page, OWNER);

    await page.goto("/konto/szkola");
    await expect(page.getByRole("heading", { level: 1, name: "Konto szkoły" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Moje szkoły" })).toBeVisible();
    await expect(page.getByRole("link", { name: OWNED_SCHOOL, exact: true })).toBeVisible();
    await expect(page.getByText("zatwierdzone przez redakcję").first()).toBeVisible();

    await page.getByRole("link", { name: OWNED_SCHOOL, exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/konto/szkola/${OWNED_SCHOOL}$`));
    await expect(page.getByRole("heading", { level: 1, name: `Profil szkoły: ${OWNED_SCHOOL}` })).toBeVisible();
    await expect(page.getByText("Zgłoszenie:", { exact: false }).first()).toBeVisible();
    const inbox = page.getByRole("region", { name: "Wiadomości od rodziców" });
    await expect(inbox.getByRole("heading", { level: 2, name: "Wiadomości od rodziców" })).toBeVisible();
    // The seed writes four synthetic enquiries to this school's inbox.
    await expect(inbox.locator("ul.plain-list > li").first()).toBeVisible();
  });
});
