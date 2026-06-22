import { test, expect } from '@playwright/test';
import { register, login, uniqueMember, ADMIN, SLIP_PNG } from './helpers';

/**
 * Flow B end-to-end across TWO users (separate browser contexts):
 *   member: renew -> issue invoice (NOT extended yet) -> upload slip (Pending)
 *   admin:  review queue -> approve slip -> invoice Paid + membership extended
 *   member: sees "ชำระเรียบร้อยแล้ว"
 */
test('membership renewal is paid by slip and confirmed by an admin', async ({ browser }) => {
  const m = uniqueMember();

  // ---- MEMBER context ----
  const memberCtx = await browser.newContext();
  const member = await memberCtx.newPage();
  await register(member, m);

  // Renew the current (trial) plan -> issues a FeeInvoice and lands on the Pay page.
  await member.goto('/Membership');
  await member.locator('.plan.current').getByRole('button', { name: /ต่ออายุ/ }).click();
  await expect(member).toHaveURL(/\/Membership\/Pay/);
  await expect(member.getByRole('heading', { name: 'ชำระค่าสมาชิก' })).toBeVisible();
  // Company bank account (from ConfigVersions) is shown to the member.
  await expect(member.getByRole('heading', { name: 'โอนเข้าบัญชีบริษัท' })).toBeVisible();

  // Fill the slip-upload form and submit.
  await member.fill('#AmountClaimed', '1000');
  await member.fill('#TransferredAt', '2026-06-19T10:00');
  await member.fill('#BankRefNote', 'E2E-REF-001');
  await member.setInputFiles('#SlipImage', { name: 'slip.png', mimeType: 'image/png', buffer: SLIP_PNG });
  await member.getByRole('button', { name: 'ส่งสลิปเพื่อตรวจสอบ' }).click();

  // Slip is now queued for review (wait for the post-submit redirect to settle).
  await member.waitForLoadState('networkidle');
  await expect(member.getByText('รอตรวจสอบ').first()).toBeVisible({ timeout: 15_000 });

  // ---- ADMIN context ----
  const adminCtx = await browser.newContext();
  const admin = await adminCtx.newPage();
  await login(admin, ADMIN.email, ADMIN.password);

  await admin.goto('/Admin/Payments');
  // Find THIS member's pending slip row and open it.
  const row = admin.locator('tr', { hasText: m.email });
  await expect(row).toBeVisible();
  await row.getByRole('link', { name: 'ตรวจสอบ' }).click();

  await expect(admin.getByRole('heading', { name: /ตรวจสอบสลิป/ })).toBeVisible();
  await admin.getByRole('button', { name: 'อนุมัติและยืนยันการชำระ' }).click();

  // Back on the queue; the slip is no longer pending for this member.
  await expect(admin).toHaveURL(/\/Admin\/Payments/);
  await expect(admin.locator('tr', { hasText: m.email })).toHaveCount(0);

  // ---- MEMBER sees the confirmed payment ----
  await member.goto('/Membership');
  await expect(member.getByText('ชำระเรียบร้อยแล้ว').first()).toBeVisible({ timeout: 15_000 });

  await memberCtx.close();
  await adminCtx.close();
});
