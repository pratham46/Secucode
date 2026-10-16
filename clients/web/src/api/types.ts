// Mirrors server/Secucode.Contracts. Keep in step with the C# records; the e2e tests run against the real API.
export type ClientKind = 'Desktop' | 'Web';
export type ExamMode = 'DesktopOnly' | 'WebAllowed';
export type AttemptStatus = 'InProgress' | 'Submitted' | 'AutoSubmitted' | 'Voided';
export type Reach = 'InScope' | 'OwnOnly';
export type Capability =
  | 'ManageSettings' | 'ManageMasters' | 'ManageRoles' | 'AssignRoles' | 'ManageUsers'
  | 'CreateExams' | 'ManageExams'
  | 'ResetAttempts' | 'ReopenAttempts' | 'ExtendTime' | 'EditQuestionsAfterAttempts'
  | 'ViewResults' | 'ViewEvidence' | 'ViewAudit';
export type AttemptEventType =
  | 'FaceMissing' | 'MultipleFaces' | 'FaceMismatch' | 'FocusLost' | 'Minimized' | 'TabHidden' | 'FullscreenExited' | 'AudioUnavailable';
export type EvidenceKind = 'ReferenceFace' | 'Webcam' | 'Screenshot' | 'Audio';

export type Language = 'Python' | 'JavaScript' | 'Java' | 'C++';
export const LANGUAGES: readonly Language[] = ['Python', 'JavaScript', 'Java', 'C++'];
export const isLanguage = (v: unknown): v is Language => LANGUAGES.includes(v as Language);

export interface ScopeDto { branchId: number | null; classId: number | null; batchId: number | null }

export interface AssignmentDto {
  id: number; roleId: number; roleName: string;
  branchId: number | null; classId: number | null; batchId: number | null;
  validFrom: string | null; validTo: string | null; isActive: boolean; isStudent: boolean;
}

export interface UserDto {
  id: number; username: string; fullName: string; email: string | null;
  isActive: boolean; hasPin: boolean; assignments: AssignmentDto[];
}

export interface EffectiveCapabilityDto { capability: Capability; reach: Reach; scope: ScopeDto; roleName: string }
export interface MeResponse { user: UserDto; capabilities: EffectiveCapabilityDto[] }
export interface LoginResponse { accessToken: string; refreshToken: string; expiresAt: string; user: UserDto }

export interface MasterDto { id: number; name: string }

export interface QuestionDto { id: number; text: string; sortOrder: number }
export interface QuestionInput { id: number | null; text: string }

export interface ExamRequest {
  title: string; startsAt: string; endsAt: string; durationMinutes: number; mode: ExamMode; isActive: boolean;
  branchId: number; classId: number | null; batchId: number | null;
  allowQuestionEditAfterAttempts: boolean; allowAttemptReset: boolean; allowAttemptReopen: boolean; allowTimeExtension: boolean;
  questions: QuestionInput[];
}

export interface ExamDto {
  id: number; title: string; startsAt: string; endsAt: string; durationMinutes: number; mode: ExamMode; isActive: boolean;
  branchId: number; classId: number | null; batchId: number | null; createdBy: number; createdByName: string;
  allowQuestionEditAfterAttempts: boolean; allowAttemptReset: boolean; allowAttemptReopen: boolean; allowTimeExtension: boolean;
  questions: QuestionDto[] | null;
}

export interface StudentExamDto {
  id: number; title: string; startsAt: string; endsAt: string; durationMinutes: number; mode: ExamMode;
  inProgressAttemptId: number | null;
}

export interface AttemptDto {
  id: number; examId: number; studentId: number; attemptNumber: number; questionText: string;
  status: AttemptStatus; client: ClientKind; startedAt: string; deadlineAt: string; submittedAt: string | null;
  language: string | null; code: string; strikes: number; submitReason: string | null; serverTime: string; lastSavedAt: string | null;
}

export interface AttemptEventDto { id: number; type: AttemptEventType; occurredAt: string; detail: string | null; evidenceId: number | null }
export interface EvidenceDto { id: number; kind: EvidenceKind; contentType: string; sizeBytes: number; createdAt: string }
export interface AttemptDetailDto { attempt: AttemptDto; studentName: string; events: AttemptEventDto[]; evidence: EvidenceDto[] }

export interface AttemptSummaryDto {
  id: number; studentId: number; studentName: string; attemptNumber: number; status: AttemptStatus; client: ClientKind;
  startedAt: string; deadlineAt: string; submittedAt: string | null; strikes: number;
}

export interface RunResult { stdout: string; stderr: string; exitCode: number; timedOut: boolean; durationMs: number }

export interface LiveAttemptMessage {
  attemptId: number; examId: number; studentId: number; status: AttemptStatus; deadlineAt: string; strikes: number;
  eventType: AttemptEventType | null;
}

export interface ProblemDetails { status?: number; title?: string; detail?: string | null; errors?: Record<string, string[]> }
