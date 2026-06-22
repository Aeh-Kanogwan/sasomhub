import { Page, expect } from '@playwright/test';

/** Seeded admin (created by DevAdminSeeder on Web startup in Development). */
export const ADMIN = { email: 'admin@neonvault.test', password: 'Admin@12345' };

/** A tiny valid 1x1 PNG used as the uploaded payment slip. */
export const SLIP_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64',
);

/** Unique member identity per test run (avoids email-collision across reruns). */
export function uniqueMember() {
  const stamp = Date.now().toString(36);
  return {
    email: `e2e_${stamp}@neonvault.test`,
    phone: '0812345678',
    password: 'Demo@12345',
  };
}

/** Register a brand-new member; ends up authenticated (Normal tier, 3-month trial). */
export async function register(page: Page, m: { email: string; phone: string; password: string }) {
  await page.goto('/Account/Register');
  await page.fill('#Email', m.email);
  await page.fill('#PhoneNumber', m.phone);
  await page.fill('#Password', m.password);
  await page.fill('#ConfirmPassword', m.password);
  await page.check('#ConsentPdpa');
  await page.getByRole('button', { name: /สมัคร/ }).click();
  // Logged in => the layout shows the "ออกจากระบบ" (logout) button.
  await expect(page.getByRole('button', { name: 'ออกจากระบบ' })).toBeVisible();
}

/** Log in via the cookie-auth login form. */
export async function login(page: Page, email: string, password: string) {
  await page.goto('/Account/Login');
  await page.fill('#Email', email);
  await page.fill('#Password', password);
  await page.getByRole('button', { name: /เข้าสู่ระบบ/ }).click();
  await expect(page.getByRole('button', { name: 'ออกจากระบบ' })).toBeVisible();
}
