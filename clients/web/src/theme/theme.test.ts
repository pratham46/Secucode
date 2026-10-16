import { describe, expect, it } from 'vitest';
import { applyTheme, loadTheme } from './theme';

describe('theme', () => {
  it('defaults to system and follows it by removing data-theme', () => {
    const root = document.createElement('html');
    root.dataset.theme = 'dark';
    expect(loadTheme()).toBe('system');
    applyTheme('system', root);
    expect(root.dataset.theme).toBeUndefined();
    expect(localStorage.getItem('secucode.theme')).toBeNull();
  });

  it('applies and remembers an explicit choice', () => {
    const root = document.createElement('html');
    applyTheme('dark', root);
    expect(root.dataset.theme).toBe('dark');
    expect(loadTheme()).toBe('dark');
  });

  it('ignores a corrupt saved value', () => {
    localStorage.setItem('secucode.theme', 'purple');
    expect(loadTheme()).toBe('system');
  });
});
