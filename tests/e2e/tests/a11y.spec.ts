import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

// Automated WCAG 2.0 A/AA and 2.1 AA scan with axe (C-02). A page fails on any serious or critical violation.
// Moderate and minor findings are reported by axe but do not fail the run, so the list stays a gate, not a backlog.
// Exceptions are pre-existing findings that cannot be fixed from tests/e2e. Each one names the route and the rule and
// must say why. Remove an entry as soon as the web app fixes it, so a stale exception cannot hide a regression.

const ROUTES = [
  "/",
  "/szkoly/demo-sp-1",
  "/ranking/country",
  "/metodologia",
  "/przedszkola",
  "/mapa",
  "/doradca",
  "/dla-szkol",
] as const;

const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21aa"] as const;
const BLOCKING_IMPACTS = new Set(["serious", "critical"]);

interface AxeException {
  readonly route: (typeof ROUTES)[number];
  readonly rule: string;
  readonly reason: string;
}

const EXCEPTIONS: readonly AxeException[] = [];

test.describe("axe WCAG 2.1 AA scan", () => {
  for (const route of ROUTES) {
    test(`${route}: no serious or critical violations`, async ({ page }) => {
      await page.goto(route);
      await page.waitForLoadState("networkidle");

      const results = await new AxeBuilder({ page }).withTags([...WCAG_TAGS]).analyze();

      const blocking = results.violations
        .filter((violation) => BLOCKING_IMPACTS.has(violation.impact ?? ""))
        .filter((violation) => !EXCEPTIONS.some((entry) => entry.route === route && entry.rule === violation.id))
        .map((violation) => ({
          rule: violation.id,
          impact: violation.impact,
          help: violation.help,
          nodes: violation.nodes.slice(0, 5).map((node) => node.target.join(" ")),
        }));

      expect(blocking, `blocking axe violations on ${route}: ${JSON.stringify(blocking, null, 2)}`).toEqual([]);
    });
  }

  test("exception list names only routes and rules this suite scans", () => {
    for (const entry of EXCEPTIONS) {
      expect(ROUTES).toContain(entry.route);
      expect(entry.reason.trim().length).toBeGreaterThan(0);
    }
  });
});
