// The exam's webcam/microphone stream, shared by the start page and the exam page (one permission prompt).
let current: MediaStream | null = null;

export function currentMedia(): MediaStream | null {
  return current;
}

export function closeMedia(): void {
  current?.getTracks().forEach(t => t.stop());
  current = null;
}
