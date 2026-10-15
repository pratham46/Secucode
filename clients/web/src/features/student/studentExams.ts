import { ApiError } from '../../api/client';
import type { StudentExamDto } from '../../api/types';

export type ExamAction =
  | { kind: 'resume'; attemptId: number }
  | { kind: 'start' }
  | { kind: 'desktop' }
  | { kind: 'upcoming'; opensAt: number };

// The PC clock may be a little off; the server makes the final call when Start is pressed.
export const START_SLACK_MS = 5 * 60_000;

export function examAction(e: StudentExamDto, now = Date.now()): ExamAction {
  if (e.mode === 'DesktopOnly') return { kind: 'desktop' };
  if (e.inProgressAttemptId !== null) return { kind: 'resume', attemptId: e.inProgressAttemptId };
  const opensAt = Date.parse(e.startsAt);
  if (now < opensAt - START_SLACK_MS) return { kind: 'upcoming', opensAt };
  // After the window the API lists an exam only when staff allowed a restart, so Start stays on.
  return { kind: 'start' };
}

export function inProgressId(error: unknown): number | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null;
  const m = /\(id (\d+)\)/.exec(error.detail ?? '');
  return m ? Number(m[1]) : null;
}
