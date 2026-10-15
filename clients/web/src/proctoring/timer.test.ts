import { describe, expect, it } from 'vitest';
import { clockOffset, formatRemaining, remainingMs, submitWindowOver } from './timer';

const local = Date.parse('2026-11-02T10:00:00Z');

describe('timer', () => {
  it('counts down on the server clock', () => {
    const offset = clockOffset('2026-11-02T10:01:00Z', local); // server is a minute ahead
    expect(offset).toBe(60_000);
    expect(remainingMs('2026-11-02T10:31:00Z', offset, local)).toBe(30 * 60_000);
    expect(remainingMs('2026-11-02T09:00:00Z', offset, local)).toBe(0);
  });

  it('allows the submit for 30 s after the deadline', () => {
    expect(submitWindowOver('2026-11-02T10:00:00Z', 0, local + 30_000)).toBe(false);
    expect(submitWindowOver('2026-11-02T10:00:00Z', 0, local + 30_001)).toBe(true);
  });

  it('formats the time left', () => {
    expect(formatRemaining(3_661_000)).toBe('1:01:01');
    expect(formatRemaining(59_500)).toBe('01:00');
    expect(formatRemaining(0)).toBe('00:00');
  });
});
