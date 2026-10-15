import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client';
import type { StudentExamDto } from '../../api/types';
import { examAction, inProgressId } from './studentExams';

const now = Date.parse('2026-11-02T10:00:00Z');
const exam = (over: Partial<StudentExamDto> = {}): StudentExamDto => ({
  id: 1, title: 'T', startsAt: '2026-11-02T10:00:00Z', endsAt: '2026-11-02T12:00:00Z', durationMinutes: 60,
  mode: 'WebAllowed', inProgressAttemptId: null, ...over,
});

describe('examAction', () => {
  it('sends desktop-only exams to the desktop app, even when in progress there', () => {
    expect(examAction(exam({ mode: 'DesktopOnly', inProgressAttemptId: 4 }), now)).toEqual({ kind: 'desktop' });
  });

  it('resumes an attempt in progress', () => {
    expect(examAction(exam({ inProgressAttemptId: 4 }), now)).toEqual({ kind: 'resume', attemptId: 4 });
  });

  it('offers Start from five minutes before the window opens', () => {
    expect(examAction(exam({ startsAt: '2026-11-02T10:05:00Z' }), now)).toEqual({ kind: 'start' });
    expect(examAction(exam({ startsAt: '2026-11-02T10:06:00Z' }), now)).toEqual({ kind: 'upcoming', opensAt: Date.parse('2026-11-02T10:06:00Z') });
  });

  it('offers Start after the window when the list still has the exam (a staff reset allowance)', () => {
    expect(examAction(exam({ endsAt: '2026-11-02T09:00:00Z' }), now)).toEqual({ kind: 'start' });
  });
});

describe('inProgressId', () => {
  it('reads the attempt id from a 409', () => {
    expect(inProgressId(new ApiError(409, 'Conflict', 'You already have an attempt in progress (id 42).'))).toBe(42);
    expect(inProgressId(new ApiError(409, 'Conflict', 'You have already submitted this exam.'))).toBeNull();
    expect(inProgressId(new ApiError(403, 'Forbidden', 'in progress (id 1)'))).toBeNull();
    expect(inProgressId(new Error('x'))).toBeNull();
  });
});
