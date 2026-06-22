import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright E2E config for the Marketplace web app (ASP.NET Core MVC, "Neon Vault").
 *
 * - Runs HEADED by default (headless:false) so you SEE the browser drive the app,
 *   with slowMo so the steps are watchable. Override for CI with `CI=1`.
 * - `webServer` auto-starts Marketplace.Web on http://localhost:5080 if it isn't
 *   already running (reuseExistingServer), wiring the SQL Server connection string.
 * - workers:1 — one browser window, and avoids races on the shared admin review queue.
 */

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:5080';
const isCI = !!process.env.CI;

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: isCI,
  retries: isCI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: BASE_URL,
    headless: process.env.HEADLESS === '1' ? true : (isCI ? true : false), // ← เปิดบราวเซอร์ให้เห็น (HEADLESS=1 หรือ CI = headless)
    launchOptions: { slowMo: isCI ? 0 : 350 }, // หน่วงให้ดูทันแต่ละสเต็ป
    viewport: { width: 1366, height: 900 },
    locale: 'th-TH',
    trace: 'on',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },

  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],

  webServer: {
    command: 'dotnet run --project ../src/Marketplace.Web --no-launch-profile',
    url: BASE_URL,
    reuseExistingServer: !isCI,
    timeout: 180_000,
    stdout: 'pipe',
    stderr: 'pipe',
    env: {
      MARKETPLACE_CONNECTION:
        'Server=localhost;Database=MarketplaceDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True',
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: BASE_URL,
    },
  },
});
