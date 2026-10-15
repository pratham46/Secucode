import { expect, test, type Page } from '@playwright/test';
import { createExam, fakeFaces, openStart, signIn, startExam, submitExam, typeCode } from './helpers';

// Screenshots for a human to check, light and dark. Not assertions about pixels.
for (const scheme of ['light', 'dark'] as const) {
  test(`screens in ${scheme}`, async ({ page, browser }) => {
    const shot = (name: string, p: Page = page) => p.screenshot({ path: `test-results/screens/${name}-${scheme}.png`, fullPage: true });
    const title = `Screens ${scheme} ${Date.now()}`;
    await createExam(title, 'WebAllowed');
    await page.emulateMedia({ colorScheme: scheme });
    await fakeFaces(page, 1);

    await page.goto('/login');
    await shot('01-sign-in');
    await signIn(page, 'student2', 'Student@123');
    await shot('02-student-exams');
    await openStart(page, title);
    await page.waitForFunction(() => document.querySelector('video')!.currentTime > 0); // a frame is on screen
    await shot('03-exam-start');
    await page.getByRole('button', { name: 'Back' }).click();
    await startExam(page, title);
    await typeCode(page, 'for i in range(3):\nprint(i)'); // CodeMirror indents after the colon
    await page.getByRole('button', { name: 'Run' }).click();
    await expect(page.getByLabel('Output', { exact: true })).toContainText('2');
    await shot('04-exam-page');
    await submitExam(page);
    await expect(page.getByRole('heading', { name: 'Submitted.' })).toBeVisible();
    await shot('05-submitted');

    const staff = await browser.newPage();
    await staff.emulateMedia({ colorScheme: scheme });
    await signIn(staff, 'faculty.cse', 'Faculty@123');
    await shot('06-staff-exams', staff);
    await staff.getByRole('button', { name: 'New exam' }).click();
    await shot('07-exam-editor', staff);
    await staff.getByRole('button', { name: 'Cancel' }).click();
    await staff.getByRole('row').filter({ hasText: title }).getByRole('button', { name: 'Attempts' }).click();
    await shot('08-attempts', staff);
    await staff.getByRole('link', { name: 'Aman Verma' }).click();
    await expect(staff.getByRole('img', { name: /^Webcam/ })).toBeVisible();
    await shot('09-attempt-detail', staff);
  });
}
