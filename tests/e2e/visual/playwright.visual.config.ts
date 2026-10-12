// Visual collector config. Separate from tests/e2e/playwright.config.ts (CI), which only picks up ./tests.
// Run through tests/e2e/visual/run.sh, or directly:
//   cd tests/e2e && pnpm exec playwright test --config visual/playwright.visual.config.ts
import { existsSync, readdirSync } from "node:fs";
import path from "node:path";
import { defineConfig } from "@playwright/test";

/**
 * Picks the chromium build under /opt/pw-browsers (never runs `playwright install`).
 * VISUAL_CHROMIUM_PATH overrides the search.
 */
function findChromium(): string | undefined {
  const override = process.env.VISUAL_CHROMIUM_PATH;
  if (override && existsSync(override)) return override;
  const root = "/opt/pw-browsers";
  if (!existsSync(root)) return undefined;
  const builds = readdirSync(root)
    .filter((name) => /^chromium-\d+$/.test(name))
    .sort((a, b) => Number(b.slice("chromium-".length)) - Number(a.slice("chromium-".length)));
  for (const build of builds) {
    const candidate = path.join(root, build, "chrome-linux", "chrome");
    if (existsSync(candidate)) return candidate;
  }
  return undefined;
}

const baseURL = process.env.VISUAL_BASE_URL ?? "http://localhost:3000";
const executablePath = findChromium();

export default defineConfig({
  testDir: __dirname,
  testMatch: /visual\.spec\.ts$/,
  outputDir: path.join(__dirname, "out", "test-results"),
  fullyParallel: false,
  workers: 2,
  retries: 0,
  timeout: 120_000,
  reporter: [["list"]],
  use: {
    baseURL,
    launchOptions: executablePath ? { executablePath } : {},
    colorScheme: "light",
    reducedMotion: "reduce",
    trace: "off",
    screenshot: "off",
    video: "off",
  },
  projects: [
    {
      name: "phone",
      use: { viewport: { width: 360, height: 740 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 },
    },
    {
      name: "tablet",
      use: { viewport: { width: 768, height: 1024 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 },
    },
    {
      name: "desktop",
      use: { viewport: { width: 1366, height: 850 }, isMobile: false, hasTouch: false, deviceScaleFactor: 1 },
    },
  ],
});
