import { describe, expect, it } from 'vitest';
import { cameraProblem } from './media';

describe('cameraProblem', () => {
  it('names what to do for each camera failure', () => {
    expect(cameraProblem(new DOMException('', 'NotAllowedError'))).toMatch(/blocked.*Allow the camera/);
    expect(cameraProblem(new DOMException('', 'NotFoundError'))).toMatch(/No webcam was found/);
    expect(cameraProblem(new DOMException('', 'NotReadableError'))).toMatch(/in use by another app/);
    expect(cameraProblem(new Error('?'))).toMatch(/couldn't be started/);
  });
});
