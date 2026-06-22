import { test, expect } from '@playwright/test';
import { register, login, uniqueMember, ADMIN } from './helpers';

/**
 * Registration (self-service + instant trial), logout, and login round-trip.
 */
test('member can register, log out, and log back in', async ({ page }) => {
  const m = uniqueMember();

  await register(page, m);

  // A fresh member gets a Normal-tier trial — the membership page marks the current plan.
  await page.goto('/Membership');
  await expect(page.locator('.plan.current')).toBeVisible();
  await expect(page.locator('.plan.current')).toContainText('แพ็กเกจปัจจุบัน');

  // Log out -> the login CTA returns.
  await page.getByRole('button', { name: 'ออกจากระบบ' }).click();
  await expect(page.getByRole('link', { name: /เข้าสู่ระบบ/ })).toBeVisible();

  // Log back in with the same credentials.
  await login(page, m.email, m.password);
});

test('admin login reveals the admin nav (RBAC)', async ({ page }) => {
  await login(page, ADMIN.email, ADMIN.password);
  // Role=Admin claim => the "ผู้ดูแล" (Admin) nav link is rendered.
  await expect(page.getByRole('link', { name: 'ผู้ดูแล' })).toBeVisible();
  await page.getByRole('link', { name: 'ผู้ดูแล' }).click();
  await expect(page).toHaveURL(/\/Admin/);
});
