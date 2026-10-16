import { isLanguage, type AttemptDto, type Language } from '../api/types';

// A copy of the student's code in this browser, so a reload or crash loses nothing that wasn't auto-saved yet.
export interface LocalCopy { code: string; language: Language; savedAt: number }

const key = (attemptId: number) => `secucode.code.${attemptId}`;

export function saveLocal(attemptId: number, code: string, language: Language): void {
  try {
    localStorage.setItem(key(attemptId), JSON.stringify({ code, language, savedAt: Date.now() }));
  } catch {
    /* storage full or blocked: the 30 s server save still runs */
  }
}

export function loadLocal(attemptId: number): LocalCopy | null {
  try {
    const v = JSON.parse(localStorage.getItem(key(attemptId)) ?? 'null') as Partial<LocalCopy> | null;
    return v && typeof v.code === 'string' && isLanguage(v.language) ? (v as LocalCopy) : null;
  } catch {
    return null;
  }
}

export function clearLocal(attemptId: number): void {
  try {
    localStorage.removeItem(key(attemptId));
  } catch {
    /* blocked */
  }
}

// The local copy wins only if it was saved after the server's last save. Its time is moved onto the server's
// clock (using the attempt's serverTime read just now) so a wrong computer clock doesn't decide it.
export function pickCode(attempt: AttemptDto, local: LocalCopy | null, now = Date.now()): { code: string; language: Language } {
  const localAt = local ? local.savedAt + (Date.parse(attempt.serverTime) - now) : 0;
  if (local && (attempt.lastSavedAt === null || localAt > Date.parse(attempt.lastSavedAt))) return { code: local.code, language: local.language };
  return { code: attempt.code, language: isLanguage(attempt.language) ? attempt.language : 'Python' };
}
