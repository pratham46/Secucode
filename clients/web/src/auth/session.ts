import type { Capability, ExamDto, MeResponse, ScopeDto } from '../api/types';

// Who is signed in and what the UI should offer them. The API stays the authority:
// this only hides actions it would refuse, and a 403 still shows the server's reason.
export class Session {
  constructor(readonly me: MeResponse) {}

  get user() {
    return this.me.user;
  }
  get isStudent(): boolean {
    return this.me.user.assignments.some(a => a.isActive && a.isStudent);
  }
  get canCreateExams(): boolean {
    return this.me.capabilities.some(c => c.capability === 'CreateExams');
  }
  get roleNames(): string {
    return [...new Set(this.me.user.assignments.filter(a => a.isActive).map(a => a.roleName))].join(', ');
  }

  canManage(exam: ExamDto): boolean {
    return this.isCreator(exam) || this.grants('ManageExams', exam);
  }
  canViewResults(exam: ExamDto): boolean {
    return this.isCreator(exam) || this.grants('ViewResults', exam);
  }

  private isCreator(exam: ExamDto): boolean {
    return exam.createdBy === this.me.user.id;
  }

  // ponytail: scope containment by ids only (same as the desktop); the API's checks cover anything this misses.
  private grants(capability: Capability, exam: ExamDto): boolean {
    return this.me.capabilities.some(c =>
      c.capability === capability && (c.reach === 'OwnOnly' ? this.isCreator(exam) : covers(c.scope, exam)));
  }
}

const covers = (s: ScopeDto, e: ExamDto) =>
  (s.branchId === null || s.branchId === e.branchId) &&
  (s.classId === null || s.classId === e.classId) &&
  (s.batchId === null || s.batchId === e.batchId);
