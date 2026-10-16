import { describe, expect, it } from 'vitest';
import { clearStrikes, loadStrikes, saveStrikes, StrikeCounter } from './strikes';

describe('StrikeCounter', () => {
  it('counts one strike per trip away, however many signals it raises', () => {
    const c = new StrikeCounter();
    expect(c.leave('FullscreenExited')).toEqual({ event: 'FullscreenExited', autoSubmit: null });
    expect(c.leave('FocusLost')).toEqual({ event: null, autoSubmit: null });
    expect(c.leave('TabHidden')).toEqual({ event: null, autoSubmit: null });
    expect(c.state.trips).toBe(1);
  });

  it('auto-submits on the third trip, exactly once', () => {
    const c = new StrikeCounter();
    c.leave('TabHidden'); c.back();
    c.leave('FocusLost'); c.back();
    expect(c.leave('FullscreenExited')).toEqual({ event: 'FullscreenExited', autoSubmit: 'Left the exam 3 times.' });
    c.back();
    expect(c.leave('TabHidden').autoSubmit).toBeNull();
  });

  it('auto-submits after three failed face checks in a row; a good check resets the run', () => {
    const c = new StrikeCounter();
    expect(c.face('missing')).toEqual({ event: 'FaceMissing', autoSubmit: null });
    expect(c.face('ok')).toEqual({ event: null, autoSubmit: null });
    c.face('missing');
    expect(c.face('multiple')).toEqual({ event: 'MultipleFaces', autoSubmit: null });
    expect(c.state.faceRun).toBe(2);
    expect(c.face('missing')).toEqual({ event: 'FaceMissing', autoSubmit: 'Face check failed 3 times in a row.' });
    expect(c.face('missing').autoSubmit).toBeNull();
  });

  it('continues from saved counts', () => {
    const c = new StrikeCounter({ trips: 2, faceRun: 0 });
    expect(c.leave('TabHidden').autoSubmit).toBe('Left the exam 3 times.');
  });
});

describe('strike persistence', () => {
  it('keeps counts across a reload of the tab and forgets corrupt values', () => {
    saveStrikes(5, { trips: 2, faceRun: 1 });
    expect(loadStrikes(5)).toEqual({ trips: 2, faceRun: 1 });
    clearStrikes(5);
    expect(loadStrikes(5)).toEqual({ trips: 0, faceRun: 0 });
    sessionStorage.setItem('secucode.strikes.5', 'junk');
    expect(loadStrikes(5)).toEqual({ trips: 0, faceRun: 0 });
  });
});
