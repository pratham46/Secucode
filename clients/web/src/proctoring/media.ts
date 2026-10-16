// The exam's webcam/microphone stream, shared by the start page and the exam page (one permission prompt).
let current: MediaStream | null = null;

export function currentMedia(): MediaStream | null {
  return current;
}

// Bumped by every close, so a getUserMedia still pending when the exam ends can tell and stop its late stream.
let generation = 0;

export function closeMedia(): void {
  generation++;
  current?.getTracks().forEach(t => t.stop());
  current = null;
}

export const hasAudio = (stream: MediaStream) => stream.getAudioTracks().length > 0;

// Camera required, microphone optional: if asking for both fails (no mic, or mic refused), try the camera alone.
// Throws the browser's error when the camera itself can't be used.
let opening: Promise<MediaStream> | null = null;

export function openMedia(): Promise<MediaStream> {
  if (current?.getVideoTracks().some(t => t.readyState === 'live')) return Promise.resolve(current);
  // Overlapping calls (React StrictMode runs effects twice in dev) share one getUserMedia, so no stream is orphaned.
  return (opening ??= open().finally(() => { opening = null; }));
}

async function open(): Promise<MediaStream> {
  closeMedia();
  const video = { width: { ideal: 640 }, height: { ideal: 480 } };
  const mine = generation;
  let stream: MediaStream;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ video, audio: true });
  } catch {
    stream = await navigator.mediaDevices.getUserMedia({ video });
  }
  if (mine !== generation) {
    stream.getTracks().forEach(t => t.stop()); // closeMedia() ran while the browser was still asking
    throw new DOMException('The camera was closed before it started.', 'AbortError');
  }
  return (current = stream);
}

// Fullscreen, plus (Chrome/Edge) keeping Esc from leaving it: a reflex Esc in the editor or a dialog would be a strike.
// Throws if fullscreen is refused; a refused keyboard lock is ignored.
export async function enterFullscreen(): Promise<void> {
  await document.documentElement.requestFullscreen();
  const keyboard = (navigator as Navigator & { keyboard?: { lock?(keys: string[]): Promise<void> } }).keyboard;
  await keyboard?.lock?.(['Escape']).catch(() => {});
}

export function browserProblem(): string | null {
  if (!window.isSecureContext) return 'Web exams need a secure connection. Open SecuCode over https (or at localhost on this computer).';
  if (!navigator.mediaDevices?.getUserMedia) return "This browser can't use a webcam. Use the latest Chrome or Edge.";
  if (!document.documentElement.requestFullscreen) return "This browser can't go fullscreen. Use the latest Chrome or Edge.";
  if (window.matchMedia?.('(pointer: coarse)').matches && !window.matchMedia('(any-pointer: fine)').matches)
    return 'Web exams need a computer with a keyboard and mouse.';
  return null;
}

export function cameraProblem(error: unknown): string {
  const name = error instanceof DOMException ? error.name : '';
  if (name === 'NotAllowedError' || name === 'SecurityError')
    return 'Camera access is blocked. Allow the camera for this site (the camera icon in the address bar), then reload the page.';
  if (name === 'NotFoundError' || name === 'OverconstrainedError') return 'No webcam was found. Connect one, then reload the page.';
  if (name === 'NotReadableError') return 'The webcam is in use by another app. Close that app, then reload the page.';
  return "The webcam couldn't be started. Reload the page to try again.";
}
