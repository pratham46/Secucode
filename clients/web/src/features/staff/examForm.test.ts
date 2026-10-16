import { describe, expect, it } from 'vitest';
import type { ExamDto } from '../../api/types';
import { emptyForm, formFromExam, fromLocalInput, move, newQuestion, rangeQuery, toLocalInput, toRequest, validate } from './examForm';

const exam: ExamDto = {
  id: 3, title: 'Arrays', startsAt: '2026-11-02T04:30:00.000Z', endsAt: '2026-11-02T06:30:00.000Z', durationMinutes: 45,
  mode: 'WebAllowed', isActive: false, branchId: 1, classId: 2, batchId: null, createdBy: 5, createdByName: 'P',
  allowQuestionEditAfterAttempts: false, allowAttemptReset: true, allowAttemptReopen: false, allowTimeExtension: true,
  questions: [{ id: 11, text: 'Second', sortOrder: 1 }, { id: 10, text: 'First', sortOrder: 0 }],
};

describe('examForm', () => {
  it('round-trips local input and UTC', () => {
    expect(fromLocalInput(toLocalInput('2026-11-02T04:30:00.000Z'))).toBe('2026-11-02T04:30:00.000Z');
    expect(toLocalInput('2026-11-02T04:30:00.000Z')).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
  });

  it('loads an exam with its questions in order', () => {
    const f = formFromExam(exam);
    expect(f.questions.map(q => [q.id, q.text])).toEqual([[10, 'First'], [11, 'Second']]);
    expect(f).toMatchObject({ title: 'Arrays', durationMinutes: '45', mode: 'WebAllowed', isActive: false, branchId: '1', classId: '2', batchId: '' });
  });

  it('builds the request: numbers, nulls, UTC times, blank questions dropped', () => {
    const f = { ...formFromExam(exam), questions: [newQuestion('  First  ', 10), newQuestion('   '), newQuestion('New')] };
    expect(toRequest(f)).toEqual({
      title: 'Arrays', startsAt: '2026-11-02T04:30:00.000Z', endsAt: '2026-11-02T06:30:00.000Z', durationMinutes: 45,
      mode: 'WebAllowed', isActive: false, branchId: 1, classId: 2, batchId: null,
      allowQuestionEditAfterAttempts: false, allowAttemptReset: true, allowAttemptReopen: false, allowTimeExtension: true,
      questions: [{ id: 10, text: 'First' }, { id: null, text: 'New' }],
    });
  });

  it('explains what is missing', () => {
    const f = { ...emptyForm(), durationMinutes: '0', questions: [newQuestion(' ')] };
    f.endsAt = f.startsAt;
    expect(validate(f)).toEqual({
      title: 'Enter a title.',
      endsAt: 'The end must be after the start.',
      durationMinutes: 'Enter a whole number of minutes above 0.',
      branchId: 'Choose a department.',
      questions: 'Add at least one question.',
    });
    expect(validate(formFromExam(exam))).toEqual({});
  });

  it('moves items within bounds only', () => {
    expect(move(['a', 'b', 'c'], 0, 1)).toEqual(['b', 'a', 'c']);
    expect(move(['a', 'b', 'c'], 2, -1)).toEqual(['a', 'c', 'b']);
    const same = ['a'];
    expect(move(same, 0, -1)).toBe(same);
  });

  it('turns a date range into whole local days in UTC', () => {
    const q = new URLSearchParams(rangeQuery({ from: '2026-11-01', to: '2026-11-03' }));
    expect(new Date(q.get('from')!).getTime()).toBe(new Date('2026-11-01T00:00').getTime());
    expect(new Date(q.get('to')!).getTime()).toBe(new Date('2026-11-03T23:59:59.999').getTime());
  });
});
