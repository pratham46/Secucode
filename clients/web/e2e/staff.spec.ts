import { expect, test } from '@playwright/test';
import { signIn } from './helpers';

test('staff create, edit and deactivate an exam', async ({ page }) => {
  const title = `E2E staff ${Date.now()}`;
  await signIn(page, 'faculty.cse', 'Faculty@123');

  await page.getByRole('button', { name: 'New exam' }).click();
  await page.getByLabel('Title').fill(title);
  await page.getByLabel('Mode').selectOption('WebAllowed');
  await page.getByLabel('Department').selectOption({ label: 'Computer Engineering' });
  await page.getByLabel('Question 1', { exact: true }).fill('Print the numbers 1 to 5.');
  await page.getByRole('button', { name: 'Save exam' }).click();

  const row = page.getByRole('row').filter({ hasText: title });
  await expect(row).toContainText('Web allowed');

  await row.getByRole('button', { name: 'Edit' }).click();
  await expect(page.getByLabel('Question 1', { exact: true })).toHaveValue('Print the numbers 1 to 5.');
  await page.getByLabel('Duration (minutes)').fill('45');
  await page.getByRole('button', { name: 'Save exam' }).click();
  await expect(row).toContainText('45 min');

  await row.getByRole('button', { name: 'Deactivate' }).click();
  await expect(row).toContainText('Inactive');
});
