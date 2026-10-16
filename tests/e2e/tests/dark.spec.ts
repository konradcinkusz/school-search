import AxeBuilder from "@axe-core/playwright";
import { expect, test, type ConsoleMessage, type Page } from "@playwright/test";

// Dark colour scheme (C-03). Runs only in the 'dark' project (colorScheme "dark"). The web app switches its tokens on
// prefers-color-scheme, so these checks prove the dark theme is actually applied, not only that the page renders.

const ROUTES = ["/", "/szkoly/demo-sp-1", "/ranking/country", "/mapa"] as const;

/** Parses a computed CSS colour such as "rgb(27, 30, 35)" or "rgba(0, 0, 0, 0)" into its channels. */
function parseRgb(value: string): { r: number; g: number; b: number; a: number } | null {
  const match = value.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)$/);
  if (!match) return null;
  return { r: Number(match[1]), g: Number(match[2]), b: Number(match[3]), a: match[4] === undefined ? 1 : Number(match[4]) };
}

/** Relative luminance (WCAG 2.x), 0 for black and 1 for white. */
function luminance({ r, g, b }: { r: number; g: number; b: number }): number {
  const channel = (value: number) => {
    const c = value / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * Degraded calls the app makes on purpose. CI runs without an auth service, so /api/auth/session answers 503 and the
 * browser logs it as a failed resource (P8). The page shows its own notice for that, so the log line is expected.
 */
const EXPECTED_DEGRADED_RESOURCES = [/\/api\/auth\/session$/];

function isExpectedDegradedResource(message: ConsoleMessage): boolean {
  if (!message.text().startsWith("Failed to load resource")) return false;
  const url = message.location().url;
  return EXPECTED_DEGRADED_RESOURCES.some((pattern) => pattern.test(url));
}

/** Opens the page and collects console errors and uncaught page errors from the first byte. */
async function openCollectingErrors(page: Page, route: string): Promise<string[]> {
  const errors: string[] = [];
  page.on("console", (message: ConsoleMessage) => {
    if (message.type() === "error" && !isExpectedDegradedResource(message)) errors.push(message.text());
  });
  page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));
  await page.goto(route);
  await page.waitForLoadState("networkidle");
  return errors;
}

test.describe("dark colour scheme", () => {
  for (const route of ROUTES) {
    test(`${route}: loads without console errors`, async ({ page }) => {
      const errors = await openCollectingErrors(page, route);
      expect(errors, `console errors on ${route}`).toEqual([]);
    });

    test(`${route}: body background is dark and colour contrast passes`, async ({ page }) => {
      await openCollectingErrors(page, route);

      const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
      const rgb = parseRgb(background);
      expect(rgb, `body background ${background} is not an opaque rgb colour`).not.toBeNull();
      expect(rgb!.a, "body background is transparent").toBeGreaterThan(0);
      expect(luminance(rgb!), `body background ${background} is not dark`).toBeLessThan(0.05);

      const contrast = await new AxeBuilder({ page }).withRules(["color-contrast"]).analyze();
      const found = contrast.violations.flatMap((violation) =>
        violation.nodes.slice(0, 5).map((node) => `${node.target.join(" ")}: ${node.failureSummary ?? violation.help}`),
      );
      expect(found, `colour-contrast violations in dark mode on ${route}`).toEqual([]);
    });
  }
});
