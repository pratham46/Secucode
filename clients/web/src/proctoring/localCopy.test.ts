import { describe, expect, it } from 'vitest';
import type { AttemptDto } from '../api/types';
import { clearLocal, loadLocal, pickCode, saveLocal } from './localCopy';

const attempt = (over: Partial<AttemptDto> = {}) => ({ code: 'server', language: 'Java', serverTime: '2026-01-01T10:00:00Z', lastSavedAt: null, ...over }) as AttemptDto;

describe('local copy', () => {
  it('prefers the local copy, else the server code with a known language', () => {
    expect(pickCode(attempt(), null)).toEqual({ code: 'server', language: 'Java' });
    expect(pickCode(attempt({ language: 'Rust' }), null)).toEqual({ code: 'server', language: 'Python' });
    expect(pickCode(attempt({ language: null }), null)).toEqual({ code: 'server', language: 'Python' });

    saveLocal(3, 'local', 'C++');
    expect(pickCode(attempt(), loadLocal(3))).toEqual({ code: 'local', language: 'C++' });

    clearLocal(3);
    expect(loadLocal(3)).toBeNull();
  });

  it('uses the local copy only when it is newer than the server save, on the server clock', () => {
    const now = Date.parse('2026-01-01T10:00:00Z');
    const local = { code: 'local', language: 'C++' as const, savedAt: now - 60_000 }; // saved 09:59 server time
    const at = (lastSavedAt: string, clockSkewMs = 0) => pickCode(attempt({ lastSavedAt }), local, now + clockSkewMs).code;
    expect(at('2026-01-01T09:58:00Z')).toBe('local');
    expect(at('2026-01-01T09:59:30Z')).toBe('server');
    expect(at('2026-01-01T09:58:00Z', 5 * 60_000)).toBe('server'); // computer clock 5 min ahead: the copy is really older
  });

  it('ignores a corrupt local copy', () => {
    localStorage.setItem('secucode.code.3', '{bad');
    expect(loadLocal(3)).toBeNull();
    localStorage.setItem('secucode.code.3', JSON.stringify({ code: 1, language: 'Python' }));
    expect(loadLocal(3)).toBeNull();
    localStorage.setItem('secucode.code.3', JSON.stringify({ code: 'x', language: 'Rust' }));
    expect(loadLocal(3)).toBeNull();
  });
});
