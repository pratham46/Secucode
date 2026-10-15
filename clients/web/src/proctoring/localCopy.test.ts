import { describe, expect, it } from 'vitest';
import type { AttemptDto } from '../api/types';
import { clearLocal, loadLocal, pickCode, saveLocal } from './localCopy';

const attempt = (over: Partial<AttemptDto> = {}) => ({ code: 'server', language: 'Java', ...over }) as AttemptDto;

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

  it('ignores a corrupt local copy', () => {
    localStorage.setItem('secucode.code.3', '{bad');
    expect(loadLocal(3)).toBeNull();
    localStorage.setItem('secucode.code.3', JSON.stringify({ code: 1, language: 'Python' }));
    expect(loadLocal(3)).toBeNull();
    localStorage.setItem('secucode.code.3', JSON.stringify({ code: 'x', language: 'Rust' }));
    expect(loadLocal(3)).toBeNull();
  });
});
