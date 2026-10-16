import { describe, expect, it, vi } from 'vitest';
import { cameraProblem } from './media';

describe('cameraProblem', () => {
  it('names what to do for each camera failure', () => {
    expect(cameraProblem(new DOMException('', 'NotAllowedError'))).toMatch(/blocked.*Allow the camera/);
    expect(cameraProblem(new DOMException('', 'NotFoundError'))).toMatch(/No webcam was found/);
    expect(cameraProblem(new DOMException('', 'NotReadableError'))).toMatch(/in use by another app/);
    expect(cameraProblem(new Error('?'))).toMatch(/couldn't be started/);
  });
});

describe('openMedia / closeMedia', () => {
  it('stops a stream that arrives after closeMedia', async () => {
    vi.resetModules();
    const stop = vi.fn();
    const stream = { getTracks: () => [{ stop }], getVideoTracks: () => [], getAudioTracks: () => [] };
    let resolve!: (s: unknown) => void;
    vi.stubGlobal('navigator', { mediaDevices: { getUserMedia: () => new Promise(r => { resolve = r; }) } });
    const m = await import('./media');
    const pending = m.openMedia();
    m.closeMedia();
    resolve(stream);
    await expect(pending).rejects.toBeInstanceOf(DOMException);
    expect(stop).toHaveBeenCalled();
    expect(m.currentMedia()).toBeNull();
    vi.unstubAllGlobals();
  });
});
