import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AudioRecorder } from './audioRecorder';

class FakeRecorder {
  static made: FakeRecorder[] = [];
  state: 'inactive' | 'recording' = 'inactive';
  ondataavailable: ((e: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  constructor(readonly stream: unknown, readonly options: unknown) { FakeRecorder.made.push(this); }
  start() { this.state = 'recording'; }
  stop() { this.state = 'inactive'; this.ondataavailable?.({ data: new Blob(['opus']) }); this.onstop?.(); }
}

beforeEach(() => {
  FakeRecorder.made = [];
  vi.useFakeTimers();
  vi.stubGlobal('MediaRecorder', FakeRecorder);
  vi.stubGlobal('MediaStream', class { constructor(readonly tracks: unknown[]) {} });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const stream = { getAudioTracks: () => ['mic'] } as unknown as MediaStream;

describe('AudioRecorder', () => {
  it('delivers a self-contained WebM chunk every interval and a last one on stop', () => {
    const chunks: Blob[] = [];
    const r = new AudioRecorder(stream, b => chunks.push(b), 60_000);
    r.start();
    vi.advanceTimersByTime(60_000);
    expect(chunks).toHaveLength(1);
    expect(chunks[0].type).toBe('audio/webm');
    expect(FakeRecorder.made).toHaveLength(2); // a new recorder per chunk, so each file has its own header

    r.stop();
    expect(chunks).toHaveLength(2);
    expect(FakeRecorder.made).toHaveLength(2); // nothing restarts after stop
    vi.advanceTimersByTime(120_000);
    expect(chunks).toHaveLength(2);
  });
});
