import { test, expect } from '@playwright/test';

/**
 * Guest (unauthenticated) browsing — the public surface renders against the live DB.
 * Selectors use exact names / .first() because the layout renders both a desktop and
 * a mobile nav (same labels appear more than once).
 */
test.describe('Guest browsing', () => {
  test('home page loads with brand + nav', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/Neon Vault/);
    await expect(page.getByRole('link', { name: 'สมาชิก', exact: true }).first()).toBeVisible();
    // Guest sees the login CTA.
    await expect(page.getByRole('link', { name: 'เข้าสู่ระบบ' }).first()).toBeVisible();
  });

  test('explore page lists catalog', async ({ page }) => {
    await page.goto('/Explore');
    await expect(page.locator('h1').first()).toContainText('การ์ด');
  });

  test('membership pricing shows the three annual tiers', async ({ page }) => {
    await page.goto('/Membership');
    await expect(page.getByRole('heading', { name: 'แพ็กเกจสมาชิก' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Normal' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Verified' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Premium' })).toBeVisible();
  });

  test('login page renders the form', async ({ page }) => {
    await page.goto('/Account/Login');
    await expect(page.locator('#Email')).toBeVisible();
    await expect(page.locator('#Password')).toBeVisible();
  });
});
