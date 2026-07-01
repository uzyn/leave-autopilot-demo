import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright config (S3-4). Runs against the app served by the repo's docker-compose.yml
 * (the `app` service, backed by the `db` Postgres service) rather than starting its own
 * server — the app/Postgres lifecycle is managed by docker compose in CI and locally (see
 * README). BASE_URL defaults to the compose service name so this works unmodified from
 * inside the `playwright` compose service; override it to hit a locally-forwarded port.
 */
export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  timeout: 30_000,
  expect: {
    timeout: 5_000,
  },
  use: {
    baseURL: process.env.BASE_URL ?? "http://app:8080",
    ignoreHTTPSErrors: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
