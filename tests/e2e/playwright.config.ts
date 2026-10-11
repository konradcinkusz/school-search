import { defineConfig, devices } from "@playwright/test";

// The running web app (e.g. http://localhost:3000 or the PR environment). CI sets PLAYWRIGHT_BASE_URL;
// BASE_URL is kept as a fallback for local runs. Both default to the port the web app listens on.
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? process.env.BASE_URL ?? "http://localhost:3000";

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
    },
  ],
});
