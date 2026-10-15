// The exam's webcam/microphone stream, shared by the start page and the exam page (one permission prompt).
let current: MediaStream | null = null;

export function currentMedia(): MediaStream | null {
  return current;
}

export function closeMedia(): void {
  current?.getTracks().forEach(t => t.stop());
  current = null;
}

export const hasAudio = (stream: MediaStream) => stream.getAudioTracks().length > 0;

// Camera required, microphone optional: if asking for both fails (no mic, or mic refused), try the camera alone.
// Throws the browser's error when the camera itself can't be used.
export async function openMedia(): Promise<MediaStream> {
  if (current?.getVideoTracks().some(t => t.readyState === 'live')) return current;
  closeMedia();
  const video = { width: { ideal: 640 }, height: { ideal: 480 } };
  try {
    current = await navigator.mediaDevices.getUserMedia({ video, audio: true });
  } catch {
    current = await navigator.mediaDevices.getUserMedia({ video });
  }
  return current;
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
