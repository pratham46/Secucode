import { describe, expect, it } from 'vitest';
import type { AssignmentDto, Capability, EffectiveCapabilityDto, ExamDto, MeResponse, Reach } from '../api/types';
import { Session } from './session';

const assignment = (over: Partial<AssignmentDto> = {}): AssignmentDto => ({
  id: 1, roleId: 1, roleName: 'Faculty', branchId: 1, classId: null, batchId: null,
  validFrom: null, validTo: null, isActive: true, isStudent: false, ...over,
});
const cap = (capability: Capability, reach: Reach = 'InScope', branchId: number | null = 1): EffectiveCapabilityDto => ({
  capability, reach, scope: { branchId, classId: null, batchId: null }, roleName: 'Faculty',
});
const me = (assignments: AssignmentDto[], capabilities: EffectiveCapabilityDto[] = []): MeResponse => ({
  user: { id: 7, username: 'u', fullName: 'U', email: null, isActive: true, hasPin: false, assignments }, capabilities,
});
const exam = (over: Partial<ExamDto> = {}): ExamDto => ({
  id: 1, title: 'T', startsAt: '', endsAt: '', durationMinutes: 30, mode: 'WebAllowed', isActive: true,
  branchId: 1, classId: 2, batchId: null, createdBy: 99, createdByName: 'X',
  allowQuestionEditAfterAttempts: true, allowAttemptReset: true, allowAttemptReopen: true, allowTimeExtension: true,
  questions: null, ...over,
});

describe('Session', () => {
  it('is a student only through an active student assignment', () => {
    expect(new Session(me([assignment({ isStudent: true })])).isStudent).toBe(true);
    expect(new Session(me([assignment({ isStudent: true, isActive: false })])).isStudent).toBe(false);
    expect(new Session(me([assignment()])).isStudent).toBe(false);
  });

  it('offers New exam only with CreateExams', () => {
    expect(new Session(me([assignment()], [cap('CreateExams')])).canCreateExams).toBe(true);
    expect(new Session(me([assignment()], [cap('ViewResults')])).canCreateExams).toBe(false);
  });

  it('lets the creator manage and view results without a grant', () => {
    const s = new Session(me([assignment()]));
    expect(s.canManage(exam({ createdBy: 7 }))).toBe(true);
    expect(s.canViewResults(exam({ createdBy: 7 }))).toBe(true);
    expect(s.canManage(exam())).toBe(false);
  });

  it('checks InScope grants against the exam target', () => {
    const s = new Session(me([assignment()], [cap('ManageExams', 'InScope', 1), cap('ViewResults', 'InScope', 2)]));
    expect(s.canManage(exam({ branchId: 1 }))).toBe(true);
    expect(s.canManage(exam({ branchId: 3 }))).toBe(false);
    expect(s.canViewResults(exam({ branchId: 1 }))).toBe(false);
    expect(new Session(me([assignment()], [cap('ManageExams', 'InScope', null)])).canManage(exam({ branchId: 5 }))).toBe(true);
  });

  it('limits OwnOnly grants to the creator', () => {
    const s = new Session(me([assignment()], [cap('ManageExams', 'OwnOnly')]));
    expect(s.canManage(exam())).toBe(false);
    expect(s.canManage(exam({ createdBy: 7 }))).toBe(true);
  });
});
