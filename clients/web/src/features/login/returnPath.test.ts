import { describe, expect, it } from 'vitest';
import { returnPath } from './LoginPage';

describe('returnPath', () => {
  it('keeps same-site paths and refuses anything else', () => {
    expect(returnPath('/exams/3/start?resume=5')).toBe('/exams/3/start?resume=5');
    expect(returnPath('//evil.example')).toBe('/exams');
    expect(returnPath('https://evil.example')).toBe('/exams');
    expect(returnPath('/login')).toBe('/exams');
    expect(returnPath(undefined)).toBe('/exams');
  });
});
