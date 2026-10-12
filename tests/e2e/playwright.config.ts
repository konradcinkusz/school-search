import { defineConfig, devices } from "@playwright/test";

// The running web app (e.g. http://localhost:3000 or the PR environment). CI sets PLAYWRIGHT_BASE_URL;
// BASE_URL is kept as a fallback for local runs. Both default to the port the web app listens on.
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? process.env.BASE_URL ?? "http://localhost:3000";

/**
 * Projects. The desktop project runs every spec except the two that need their own viewport or colour scheme, so
 * those specs run once, in their own project, and are not duplicated three times.
 *   - chromium: desktop Chrome, every spec that is not listed below.
 *   - phone:    360x740 touch viewport with isMobile (phone.spec.ts only).
 *   - dark:     colorScheme "dark" (dark.spec.ts only).
 */
const ownProjectSpecs = [/phone\.spec\.ts$/, /dark\.spec\.ts$/];

export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL,
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
      testIgnore: ownProjectSpecs,
    },
    {
      name: "phone",
      testMatch: /phone\.spec\.ts$/,
      use: {
        ...devices["Desktop Chrome"],
        viewport: { width: 360, height: 740 },
        isMobile: true,
        hasTouch: true,
      },
    },
    {
      name: "dark",
      testMatch: /dark\.spec\.ts$/,
      use: {
        ...devices["Desktop Chrome"],
        colorScheme: "dark",
      },
    },
  ],
});
