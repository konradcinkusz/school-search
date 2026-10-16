import { expect, test, type Page } from "@playwright/test";

// Phone layout (C-01). Runs only in the 'phone' project: 360x740, isMobile, touch. Each route is checked for three
// things: no sideways page scroll, every overflowing .table-wrap is a keyboard-reachable named scroll region or a
// stacked table, and every visible control (buttons, inputs, nav and footer links) is at least 44 CSS px tall.
// The routes are the public pages a phone visitor reaches. The demo seed is the real catalog data (see DemoSeed.cs).

const ROUTES = [
  "/",
  "/szkoly/demo-sp-1",
  "/szkoly/demo-sp-3",
  "/ranking/country",
  "/porownaj?slugs=demo-sp-1,demo-sp-7",
  "/przedszkola",
  "/mapa",
  "/przedszkola/przedszkole-demonstracyjne-nr-1",
  "/metodologia",
  "/dla-szkol",
  "/doradca",
  "/cennik",
] as const;

const MIN_TARGET_PX = 44;

/**
 * Pre-existing target-size gaps that cannot be fixed from tests/e2e. Each entry names the route and the selector
 * fragment of the control. Keep this list short and dated; remove an entry the moment the web app fixes it.
 */
const TARGET_SIZE_EXCEPTIONS: ReadonlyArray<{ route: string; selector: string; reason: string }> = [];

/** Page open: wait for the client bundle, so hydration-time layout shifts are settled before measuring. */
async function openPhonePage(page: Page, route: string): Promise<void> {
  await page.goto(route);
  await page.waitForLoadState("networkidle");
}

interface WrapReport {
  readonly index: number;
  readonly overflows: boolean;
  readonly focusableRegion: boolean;
  readonly stacked: boolean;
  readonly outerHtml: string;
}

interface TargetReport {
  readonly selector: string;
  readonly label: string;
  readonly height: number;
  readonly width: number;
}

test.describe("phone layout at 360x740", () => {
  for (const route of ROUTES) {
    test(`${route}: no sideways page scroll`, async ({ page }) => {
      await openPhonePage(page, route);
      const widths = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
      }));
      expect(widths.scrollWidth, `scrollWidth ${widths.scrollWidth} > clientWidth ${widths.clientWidth}`).toBeLessThanOrEqual(
        widths.clientWidth,
      );
    });

    test(`${route}: overflowing tables are named scroll regions or stacked`, async ({ page }) => {
      await openPhonePage(page, route);
      const wraps = await page.evaluate((): WrapReport[] =>
        Array.from(document.querySelectorAll<HTMLElement>(".table-wrap")).map((el, index) => {
          const table = el.querySelector("table");
          const row = table?.querySelector("tr");
          return {
            index,
            overflows: el.scrollWidth > el.clientWidth + 1,
            focusableRegion:
              el.getAttribute("tabindex") === "0" &&
              el.getAttribute("role") === "region" &&
              (el.getAttribute("aria-label") ?? "").trim() !== "",
            stacked: table !== null && table.classList.contains("table--stack") && row !== undefined && row !== null
              ? getComputedStyle(row).display === "block"
              : false,
            outerHtml: el.outerHTML.slice(0, 160),
          };
        }),
      );
      const offending = wraps.filter((wrap) => wrap.overflows && !wrap.focusableRegion && !wrap.stacked);
      expect(offending, `overflowing .table-wrap without a named scroll region or stacking: ${JSON.stringify(offending)}`).toEqual([]);
    });

    test(`${route}: buttons, inputs and nav/footer links are at least 44px tall`, async ({ page }) => {
      await openPhonePage(page, route);
      const targets = await page.evaluate((): TargetReport[] => {
        const selector = "button, input:not([type=hidden]), select, textarea, nav a, footer a";
        return Array.from(document.querySelectorAll<HTMLElement>(selector))
          .filter((el) => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden")
          .filter((el) => !el.closest("[aria-hidden='true']"))
          .map((el) => {
            // A checkbox or radio is a 13-24px glyph inside its label; the label is the touch target (WCAG 2.5.8).
            const isChoice = el instanceof HTMLInputElement && (el.type === "checkbox" || el.type === "radio");
            const box = (isChoice ? el.closest("label") ?? el : el).getBoundingClientRect();
            const name = (el.getAttribute("aria-label") ?? el.textContent ?? el.getAttribute("name") ?? "").trim();
            return {
              selector: el.tagName.toLowerCase() + (el.id ? `#${el.id}` : ""),
              label: name.slice(0, 60),
              height: Math.round(box.height * 10) / 10,
              width: Math.round(box.width * 10) / 10,
            };
          });
      });
      const excepted = (target: TargetReport) =>
        TARGET_SIZE_EXCEPTIONS.some((entry) => entry.route === route && target.selector.includes(entry.selector));
      const small = targets.filter((target) => target.height < MIN_TARGET_PX && !excepted(target));
      expect(small, `controls shorter than ${MIN_TARGET_PX}px: ${JSON.stringify(small)}`).toEqual([]);
    });
  }
});
