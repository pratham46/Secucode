import { expect, test } from '@playwright/test';
import { apiAs, createExam, fakeFaces, setFaces, signIn, startExam, submitExam, typeCode } from './helpers';

const submitted = (page: import('@playwright/test').Page) => expect(page.getByRole('heading', { name: 'Submitted.' })).toBeVisible({ timeout: 40_000 });

test.beforeEach(async ({ page }) => { await fakeFaces(page, 1); });

test('a student takes a web exam and staff see the code and evidence', async ({ page, browser }) => {
  const title = `E2E web ${Date.now()}`;
  const desktopTitle = `E2E desktop ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await createExam(desktopTitle, 'DesktopOnly');

  await signIn(page, 'student1', 'Student@123');
  const desktopCard = page.getByRole('article').filter({ hasText: desktopTitle });
  await expect(desktopCard).toContainText('Desktop app only');
  await expect(desktopCard.getByRole('button')).toHaveCount(0);

  await startExam(page, title);
  await page.getByLabel('Language').selectOption('JavaScript');
  await page.getByLabel('Language').selectOption('Python');
  await typeCode(page, 'print("hello web")');
  await page.getByRole('button', { name: 'Run' }).click();
  await expect(page.getByLabel('Output', { exact: true })).toContainText('hello web');
  await page.getByRole('button', { name: 'Submit', exact: true }).click();
  await page.getByRole('dialog', { name: 'Submit your exam?' }).getByRole('button', { name: 'Keep working' }).click();
  await expect(page.getByText('Left exam: 0 of 3')).toBeVisible(); // dropdown, Run and the dialog are not leaving
  await submitExam(page);
  await submitted(page);

  const staff = await browser.newPage();
  await signIn(staff, 'faculty.cse', 'Faculty@123');
  await staff.getByRole('row').filter({ hasText: title }).getByRole('button', { name: 'Attempts' }).click();
  await staff.getByRole('link', { name: 'Neha Sharma' }).click();
  await expect(staff.getByLabel('Submitted code')).toContainText('hello web');
  await expect(staff.getByRole('img', { name: /^ReferenceFace/ })).toBeVisible();
  await expect(staff.getByRole('img', { name: /^Webcam/ })).toBeVisible();
});

test('reloading mid-exam keeps the code and resumes', async ({ page }) => {
  const title = `E2E reload ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await signIn(page, 'student1', 'Student@123');
  await startExam(page, title);
  await typeCode(page, 'x = 41');
  await page.evaluate(() => document.exitFullscreen());
  await page.getByRole('button', { name: 'Return to fullscreen' }).click();
  await expect(page.getByText('Left exam: 1 of 3')).toBeVisible();
  const localCode = () => page.evaluate(() => Object.entries(localStorage).find(([k]) => k.startsWith('secucode.code.'))?.[1] ?? '');
  await expect.poll(localCode).toContain('x = 41'); // the local copy is written 1 s after the last keystroke

  await page.reload();
  await expect(page.getByText('Face detected.')).toBeVisible();
  await page.getByRole('button', { name: 'Resume exam' }).click();
  await expect(page.getByLabel('Your code')).toContainText('x = 41');
  // The strike survived (1) and the reload itself left fullscreen, which is a second trip.
  await expect(page.getByText('Left exam: 2 of 3')).toBeVisible();
});

test('leaving fullscreen three times submits the exam', async ({ page }) => {
  const title = `E2E fullscreen ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await signIn(page, 'student1', 'Student@123');
  await startExam(page, title);

  for (let i = 1; i <= 2; i++) {
    await page.evaluate(() => document.exitFullscreen());
    await page.getByRole('button', { name: 'Return to fullscreen' }).click();
    await expect(page.getByText(`Left exam: ${i} of 3`)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Return to fullscreen' })).toBeHidden();
  }
  await page.evaluate(() => document.exitFullscreen());
  await submitted(page);
});

test('three failed face checks in a row submit the exam', async ({ page }) => {
  const title = `E2E face ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await signIn(page, 'student1', 'Student@123');
  await startExam(page, title);
  await setFaces(page, 0);
  await submitted(page);
});

test('work done offline is kept and sent when the connection returns', async ({ page, context }) => {
  const title = `E2E offline ${Date.now()}`;
  const exam = await createExam(title, 'WebAllowed');
  await signIn(page, 'student2', 'Student@123');
  await startExam(page, title);
  await typeCode(page, 'print("online")');

  await context.setOffline(true);
  await page.keyboard.type('\nprint("offline-typed")');
  await page.evaluate(() => document.exitFullscreen()); // the event can't be sent, so the queue notices it is offline
  await page.getByRole('button', { name: 'Return to fullscreen' }).click();
  await expect(page.getByText('Your work is kept in this tab and will be sent when the connection returns.')).toBeVisible();
  await submitExam(page);
  await expect(page.getByText('Your code will be submitted when the connection returns.')).toBeVisible();
  await context.setOffline(false);
  await submitted(page);

  const faculty = await apiAs('faculty.cse', 'Faculty@123');
  const [attempt] = (await (await faculty.get(`/api/exams/${exam.id}/attempts`)).json()) as { id: number }[];
  const detail = (await (await faculty.get(`/api/attempts/${attempt.id}`)).json()) as { attempt: { code: string; status: string }; events: { type: string }[] };
  expect(detail.attempt.status).toBe('Submitted');
  expect(detail.attempt.code).toContain('offline-typed');
  expect(detail.events.map(e => e.type)).toContain('FullscreenExited');
});
