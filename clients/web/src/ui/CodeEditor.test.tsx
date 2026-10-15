import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CodeEditor } from './CodeEditor';

const fire = (target: Element, type: string) => {
  const e = new Event(type, { bubbles: true, cancelable: true });
  target.dispatchEvent(e);
  return e.defaultPrevented;
};

describe('CodeEditor', () => {
  it('shows the code and labels the editable area', () => {
    const { container } = render(<CodeEditor value={'print(1)'} language="Python" label="Your code" />);
    const content = container.querySelector('.cm-content')!;
    expect(content.textContent).toContain('print(1)');
    expect(content.getAttribute('aria-label')).toBe('Your code');
  });

  it('blocks clipboard, drop and the context menu when locked', () => {
    const { container } = render(<CodeEditor value="" language="Python" label="Code" locked />);
    const content = container.querySelector('.cm-content')!;
    for (const type of ['paste', 'copy', 'cut', 'drop', 'contextmenu']) expect(fire(content, type)).toBe(true);
  });

  it('leaves the context menu alone when not locked', () => {
    const { container } = render(<CodeEditor value="" language="Python" label="Code" />);
    expect(fire(container.querySelector('.cm-content')!, 'contextmenu')).toBe(false);
  });
});
