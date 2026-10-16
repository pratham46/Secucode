import { afterEach, describe, expect, it } from 'vitest';
import { classify, countFaces } from './faceMonitor';

afterEach(() => { delete window.__secucodeFakeFaces; });

describe('faceMonitor', () => {
  it('classifies face counts', () => {
    expect([0, 1, 2, 5].map(classify)).toEqual(['missing', 'ok', 'multiple', 'multiple']);
  });

  it('sees no face when the camera is not running', async () => {
    expect(await countFaces(document.createElement('video'))).toBe(0);
  });

  it('uses the test override in development builds', async () => {
    window.__secucodeFakeFaces = 2;
    expect(await countFaces(document.createElement('video'))).toBe(2);
  });
});
