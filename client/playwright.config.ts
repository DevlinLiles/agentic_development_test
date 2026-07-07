import { defineConfig, devices } from "@playwright/test";

// These E2E tests drive the real app in a real browser against the real backend.
// The API + SQL Server are NOT started here (they need docker compose + dotnet run,
// per the repo README) — start those yourself before running `npm run test:e2e`.
// This config only manages the Vite dev server for the client half of the stack.
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  retries: 0,
  reporter: "list",
  use: {
    baseURL: "http://localhost:5173",
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command: "npm run dev",
    url: "http://localhost:5173",
    reuseExistingServer: true,
    timeout: 30_000,
  },
});
