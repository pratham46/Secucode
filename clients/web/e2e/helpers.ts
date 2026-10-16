import { expect, request, type APIRequestContext, type Page } from '@playwright/test';
import { API_URL } from './global-setup';

export async function apiAs(username: string, password: string): Promise<APIRequestContext> {
  const anon = await request.newContext({ baseURL: API_URL });
  const res = await anon.post('/api/auth/login', { data: { username, password, client: 'Desktop' } });
  expect(res.ok()).toBe(true);
  const { accessToken } = (await res.json()) as { accessToken: string };
  return request.newContext({ baseURL: API_URL, extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` } });
}

// An exam open now for the whole Computer Engineering department (student1 and student2 are in it).
export async function createExam(title: string, mode: 'WebAllowed' | 'DesktopOnly', durationMinutes = 30): Promise<{ id: number }> {
  const faculty = await apiAs('faculty.cse', 'Faculty@123');
  const branches = (await (await faculty.get('/api/branches')).json()) as { id: number; name: string }[];
  const now = Date.now();
  const res = await faculty.post('/api/exams', {
    data: {
      title, mode, durationMinutes, isActive: true,
      startsAt: new Date(now - 60_000).toISOString(), endsAt: new Date(now + 3_600_000).toISOString(),
      branchId: branches.find(b => b.name === 'Computer Engineering')!.id, classId: null, batchId: null,
      allowQuestionEditAfterAttempts: true, allowAttemptReset: true, allowAttemptReopen: true, allowTimeExtension: true,
      questions: [{ id: null, text: 'Print a greeting.' }],
    },
  });
  expect(res.status()).toBe(201);
  return (await res.json()) as { id: number };
}

// The fake camera shows a test pattern, not a face; DEV builds read the face count from this global.
export const fakeFaces = (page: Page, count: number) => page.addInitScript(c => { (window as unknown as { __secucodeFakeFaces?: number }).__secucodeFakeFaces = c; }, count);
export const setFaces = (page: Page, count: number) => page.evaluate(c => { (window as unknown as { __secucodeFakeFaces?: number }).__secucodeFakeFaces = c; }, count);

export async function signIn(page: Page, username: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Exams' })).toBeVisible();
}

export async function openStart(page: Page, title: string) {
  await page.getByRole('article').filter({ hasText: title }).getByRole('button', { name: 'Start' }).click();
  await expect(page.getByText('Face detected.')).toBeVisible();
}

export async function startExam(page: Page, title: string) {
  await openStart(page, title);
  await page.getByLabel('PIN').fill('1234');
  await page.getByRole('button', { name: 'Start exam' }).click();
  await expect(page.getByRole('timer')).toBeVisible();
}

export async function typeCode(page: Page, text: string) {
  await page.getByLabel('Your code').click();
  await page.keyboard.type(text);
}

export async function submitExam(page: Page) {
  await page.getByRole('button', { name: 'Submit', exact: true }).click();
  await page.getByRole('dialog', { name: 'Submit your exam?' }).getByRole('button', { name: 'Submit' }).click();
}
