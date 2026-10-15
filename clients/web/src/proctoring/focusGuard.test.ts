import { afterEach, describe, expect, it, vi } from 'vitest';
import { watchFocus } from './focusGuard';

let fullscreen: Element | null = null;
let visibility: DocumentVisibilityState = 'visible';
let focused = true;

Object.defineProperty(document, 'fullscreenElement', { configurable: true, get: () => fullscreen });
Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
vi.spyOn(document, 'hasFocus').mockImplementation(() => focused);

afterEach(() => { fullscreen = document.body; visibility = 'visible'; focused = true; });

describe('watchFocus', () => {
  it('reports each way of leaving, and comes back only when fully present again', () => {
    fullscreen = document.body;
    const left: string[] = [];
    let back = 0;
    const stop = watchFocus(s => left.push(s), () => back++);

    fullscreen = null;
    document.dispatchEvent(new Event('fullscreenchange'));
    focused = false;
    window.dispatchEvent(new Event('blur'));
    visibility = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));
    expect(left).toEqual(['FullscreenExited', 'FocusLost', 'TabHidden']);

    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange')); // still unfocused and not fullscreen
    focused = true;
    window.dispatchEvent(new Event('focus')); // still not fullscreen
    expect(back).toBe(0);

    fullscreen = document.body;
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(back).toBe(1);

    stop();
    fullscreen = null;
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(left).toHaveLength(3);
  });
});
