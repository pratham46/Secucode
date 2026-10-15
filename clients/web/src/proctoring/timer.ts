// The server clock decides exam time; the browser only tracks its offset from it.
export const SUBMIT_GRACE_MS = 30_000;

export const clockOffset = (serverTimeIso: string, localNow = Date.now()) => Date.parse(serverTimeIso) - localNow;

export const remainingMs = (deadlineIso: string, offsetMs: number, localNow = Date.now()) =>
  Math.max(0, Date.parse(deadlineIso) - (localNow + offsetMs));

export const submitWindowOver = (deadlineIso: string, offsetMs: number, localNow = Date.now()) =>
  localNow + offsetMs > Date.parse(deadlineIso) + SUBMIT_GRACE_MS;

export function formatRemaining(ms: number): string {
  const total = Math.ceil(ms / 1000);
  const h = Math.floor(total / 3600);
  const mm = String(Math.floor((total % 3600) / 60)).padStart(2, '0');
  const ss = String(total % 60).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}
