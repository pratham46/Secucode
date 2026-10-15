// Strike rules shared with the desktop app: 3 trips away from the exam, or 3 failed face checks in a row, submit it.
export type LeaveSignal = 'TabHidden' | 'FocusLost' | 'FullscreenExited';
export type FaceResult = 'ok' | 'missing' | 'multiple';
export const MAX_STRIKES = 3;

export interface StrikeState { trips: number; faceRun: number }
export interface Outcome<E> { event: E | null; autoSubmit: string | null }

export class StrikeCounter {
  private away = false;

  constructor(readonly state: StrikeState = { trips: 0, faceRun: 0 }) {}

  // Leaving fires several signals at once (exit fullscreen + blur + hidden); only the first of a trip counts.
  leave(signal: LeaveSignal): Outcome<LeaveSignal> {
    if (this.away) return { event: null, autoSubmit: null };
    this.away = true;
    this.state.trips++;
    return { event: signal, autoSubmit: this.state.trips === MAX_STRIKES ? `Left the exam ${MAX_STRIKES} times.` : null };
  }

  back(): void {
    this.away = false;
  }

  face(result: FaceResult): Outcome<'FaceMissing' | 'MultipleFaces'> {
    if (result === 'ok') {
      this.state.faceRun = 0;
      return { event: null, autoSubmit: null };
    }
    this.state.faceRun++;
    return {
      event: result === 'missing' ? 'FaceMissing' : 'MultipleFaces',
      autoSubmit: this.state.faceRun === MAX_STRIKES ? `Face check failed ${MAX_STRIKES} times in a row.` : null,
    };
  }
}

const key = (attemptId: number) => `secucode.strikes.${attemptId}`;

export function loadStrikes(attemptId: number): StrikeState {
  try {
    const v = JSON.parse(sessionStorage.getItem(key(attemptId)) ?? 'null') as Partial<StrikeState> | null;
    if (v && typeof v.trips === 'number' && typeof v.faceRun === 'number') return { trips: v.trips, faceRun: v.faceRun };
  } catch {
    /* corrupt or blocked: start from zero */
  }
  return { trips: 0, faceRun: 0 };
}

export function saveStrikes(attemptId: number, state: StrikeState): void {
  try {
    sessionStorage.setItem(key(attemptId), JSON.stringify(state));
  } catch {
    /* blocked: counts last until reload */
  }
}

export function clearStrikes(attemptId: number): void {
  try {
    sessionStorage.removeItem(key(attemptId));
  } catch {
    /* blocked */
  }
}
