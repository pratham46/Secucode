import type { ExamDto, ExamMode, ExamRequest } from '../../api/types';

export interface QuestionDraft { key: string; id: number | null; text: string }

// Form state is strings (what the inputs hold); toRequest converts once, on save.
export interface ExamForm {
  title: string;
  startsAt: string; // datetime-local value, local time
  endsAt: string;
  durationMinutes: string;
  mode: ExamMode;
  isActive: boolean;
  branchId: string;
  classId: string;
  batchId: string;
  allowQuestionEditAfterAttempts: boolean;
  allowAttemptReset: boolean;
  allowAttemptReopen: boolean;
  allowTimeExtension: boolean;
  questions: QuestionDraft[];
}

export type FormErrors = Partial<Record<keyof ExamForm, string>>;

const pad = (n: number) => String(n).padStart(2, '0');

export function toLocalInput(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// "2026-11-02T10:00" without an offset is local time by the JS spec.
export const fromLocalInput = (value: string) => new Date(value).toISOString();

let nextKey = 0;
export const newQuestion = (text = '', id: number | null = null): QuestionDraft => ({ key: `q${nextKey++}`, id, text });

export function emptyForm(now = new Date()): ExamForm {
  const start = new Date(now);
  start.setMinutes(0, 0, 0);
  start.setHours(start.getHours() + 1);
  const end = new Date(start.getTime() + 2 * 3_600_000);
  return {
    title: '', startsAt: toLocalInput(start.toISOString()), endsAt: toLocalInput(end.toISOString()), durationMinutes: '60',
    mode: 'DesktopOnly', isActive: true, branchId: '', classId: '', batchId: '',
    allowQuestionEditAfterAttempts: true, allowAttemptReset: true, allowAttemptReopen: true, allowTimeExtension: true,
    questions: [newQuestion()],
  };
}

const idText = (v: number | null) => (v === null ? '' : String(v));

export function formFromExam(e: ExamDto): ExamForm {
  return {
    title: e.title, startsAt: toLocalInput(e.startsAt), endsAt: toLocalInput(e.endsAt), durationMinutes: String(e.durationMinutes),
    mode: e.mode, isActive: e.isActive, branchId: String(e.branchId), classId: idText(e.classId), batchId: idText(e.batchId),
    allowQuestionEditAfterAttempts: e.allowQuestionEditAfterAttempts, allowAttemptReset: e.allowAttemptReset,
    allowAttemptReopen: e.allowAttemptReopen, allowTimeExtension: e.allowTimeExtension,
    questions: [...(e.questions ?? [])].sort((a, b) => a.sortOrder - b.sortOrder).map(q => newQuestion(q.text, q.id)),
  };
}

export function validate(f: ExamForm): FormErrors {
  const errors: FormErrors = {};
  if (!f.title.trim()) errors.title = 'Enter a title.';
  if (!f.startsAt || !f.endsAt) errors.endsAt = 'Enter the start and end.';
  else if (new Date(f.endsAt) <= new Date(f.startsAt)) errors.endsAt = 'The end must be after the start.';
  const minutes = Number(f.durationMinutes);
  if (!Number.isInteger(minutes) || minutes <= 0) errors.durationMinutes = 'Enter a whole number of minutes above 0.';
  if (!f.branchId) errors.branchId = 'Choose a department.';
  if (!f.questions.some(q => q.text.trim())) errors.questions = 'Add at least one question.';
  return errors;
}

const optionalId = (v: string) => (v ? Number(v) : null);

export function toRequest(f: ExamForm): ExamRequest {
  return {
    title: f.title.trim(), startsAt: fromLocalInput(f.startsAt), endsAt: fromLocalInput(f.endsAt),
    durationMinutes: Number(f.durationMinutes), mode: f.mode, isActive: f.isActive,
    branchId: Number(f.branchId), classId: optionalId(f.classId), batchId: optionalId(f.batchId),
    allowQuestionEditAfterAttempts: f.allowQuestionEditAfterAttempts, allowAttemptReset: f.allowAttemptReset,
    allowAttemptReopen: f.allowAttemptReopen, allowTimeExtension: f.allowTimeExtension,
    questions: f.questions.filter(q => q.text.trim()).map(q => ({ id: q.id, text: q.text.trim() })),
  };
}

export function move<T>(list: T[], index: number, delta: -1 | 1): T[] {
  const to = index + delta;
  if (to < 0 || to >= list.length) return list;
  const copy = [...list];
  [copy[index], copy[to]] = [copy[to], copy[index]];
  return copy;
}

export interface DateRange { from: string; to: string } // yyyy-mm-dd, local

const day = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

export function dateRangeDefaults(now = new Date()): DateRange {
  const from = new Date(now);
  from.setDate(from.getDate() - 7);
  const to = new Date(now);
  to.setDate(to.getDate() + 30);
  return { from: day(from), to: day(to) };
}

export function rangeQuery(r: DateRange): string {
  return new URLSearchParams({
    from: new Date(`${r.from}T00:00`).toISOString(),
    to: new Date(`${r.to}T23:59:59.999`).toISOString(),
  }).toString();
}
