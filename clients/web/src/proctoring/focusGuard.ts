import type { LeaveSignal } from './strikes';

// Reports every sign of leaving the exam, and "back" once the page is visible, focused and fullscreen again.
// Browser shortcuts (Alt+Tab, Win key) can't be blocked from a page; leaving that way is caught here instead.
export function watchFocus(onLeave: (s: LeaveSignal) => void, onBack: () => void, doc: Document = document, win: Window = window): () => void {
  const present = () => doc.visibilityState === 'visible' && doc.hasFocus() && doc.fullscreenElement !== null;
  const maybeBack = () => { if (present()) onBack(); };
  const onVisibility = () => (doc.visibilityState === 'hidden' ? onLeave('TabHidden') : maybeBack());
  const onBlur = () => onLeave('FocusLost');
  const onFullscreen = () => (doc.fullscreenElement ? maybeBack() : onLeave('FullscreenExited'));

  doc.addEventListener('visibilitychange', onVisibility);
  win.addEventListener('blur', onBlur);
  win.addEventListener('focus', maybeBack);
  doc.addEventListener('fullscreenchange', onFullscreen);
  return () => {
    doc.removeEventListener('visibilitychange', onVisibility);
    win.removeEventListener('blur', onBlur);
    win.removeEventListener('focus', maybeBack);
    doc.removeEventListener('fullscreenchange', onFullscreen);
  };
}
