// Records the microphone in fixed-length chunks. Each chunk gets its own MediaRecorder, so every uploaded
// file is a complete WebM that plays on its own (timeslice chunks after the first have no header).
export class AudioRecorder {
  private recorder: MediaRecorder | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private stopped = true;

  constructor(
    private readonly stream: MediaStream,
    private readonly onChunk: (blob: Blob) => void,
    private readonly chunkMs = 60_000,
  ) {}

  start(): void {
    this.stopped = false;
    this.next();
  }

  // The current partial chunk is still delivered.
  stop(): void {
    this.stopped = true;
    clearTimeout(this.timer);
    if (this.recorder && this.recorder.state !== 'inactive') this.recorder.stop();
  }

  private next(): void {
    if (this.stopped || !this.stream.getAudioTracks().some(t => t.readyState === 'live')) return; // mic unplugged: stop quietly
    const recorder = new MediaRecorder(new MediaStream(this.stream.getAudioTracks()), { mimeType: 'audio/webm;codecs=opus' });
    const parts: Blob[] = [];
    recorder.ondataavailable = e => { if (e.data.size > 0) parts.push(e.data); };
    recorder.onstop = () => {
      if (parts.length > 0) this.onChunk(new Blob(parts, { type: 'audio/webm' })); // the API accepts exactly "audio/webm"
      this.next();
    };
    recorder.start();
    this.recorder = recorder;
    this.timer = setTimeout(() => { if (recorder.state !== 'inactive') recorder.stop(); }, this.chunkMs);
  }
}
