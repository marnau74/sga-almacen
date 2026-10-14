import { defineConfig, devices } from "@playwright/test";

/**
 * De extremo a extremo contra la aplicación construida: el front dentro de la API (wwwroot) y la API con la base de
 * demostración sobre el PostgreSQL del compose (`docker compose up -d postgres`).
 */
const PUERTO = 5080;

export default defineConfig({
  testDir: "e2e",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: { baseURL: `http://localhost:${PUERTO}`, locale: "es-ES", timezoneId: "Europe/Madrid", trace: "retain-on-failure" },
  projects: [
    { name: "oficina", use: { ...devices["Desktop Chrome"] }, testMatch: /oficina\.spec\.ts/ },
    { name: "terminal", use: { ...devices["Pixel 7"] }, testMatch: /terminal\.spec\.ts/ },
  ],
  webServer: {
    command: `npm run build && dotnet run --project ../src/Sga.Api --no-launch-profile --urls http://localhost:${PUERTO}`,
    url: `http://localhost:${PUERTO}/health`,
    timeout: 600_000,
    reuseExistingServer: !process.env.CI,
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
  },
});
