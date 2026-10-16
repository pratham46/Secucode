import { cpp } from '@codemirror/lang-cpp';
import { java } from '@codemirror/lang-java';
import { javascript } from '@codemirror/lang-javascript';
import { python } from '@codemirror/lang-python';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { basicSetup } from 'codemirror';
import { useEffect, useRef } from 'react';
import type { Language } from '../api/types';

const languageSupport = (l: Language) =>
  l === 'Python' ? python() : l === 'JavaScript' ? javascript() : l === 'Java' ? java() : cpp();

const stop = (e: Event) => { e.preventDefault(); return true; };
const blockClipboard = EditorView.domEventHandlers({ paste: stop, copy: stop, cut: stop, drop: stop, contextmenu: stop });

const theme = EditorView.theme({
  '&': { height: '100%', backgroundColor: 'var(--surface-raised)', color: 'var(--text-primary)', fontSize: '14px' },
  '&.cm-focused': { outline: '2px solid var(--focus)', outlineOffset: '-2px' },
  '.cm-scroller': { fontFamily: 'var(--font-code)' },
  '.cm-content': { caretColor: 'var(--text-primary)' },
  '.cm-gutters': { backgroundColor: 'var(--surface-sunken)', color: 'var(--text-secondary)', borderRight: '1px solid var(--border)' },
  '.cm-activeLine, .cm-activeLineGutter': { backgroundColor: 'var(--subtle-hover)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': { backgroundColor: 'var(--accent-soft)' },
});

export function CodeEditor({ value, language, onChange, readOnly = false, locked = false, label }:
  { value: string; language: Language; onChange?(code: string): void; readOnly?: boolean; locked?: boolean; label: string }) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const languageSlot = useRef(new Compartment());
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  // Created once; value and language changes are pushed in by the effects below.
  useEffect(() => {
    const v = new EditorView({
      parent: host.current!,
      state: EditorState.create({
        doc: value,
        extensions: [
          basicSetup,
          theme,
          languageSlot.current.of(languageSupport(language)),
          EditorState.readOnly.of(readOnly),
          EditorView.editable.of(!readOnly),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          locked ? blockClipboard : [],
          EditorView.updateListener.of(u => { if (u.docChanged) onChangeRef.current?.(u.state.doc.toString()); }),
        ],
      }),
    });
    view.current = v;
    return () => { v.destroy(); view.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    view.current?.dispatch({ effects: languageSlot.current.reconfigure(languageSupport(language)) });
  }, [language]);

  useEffect(() => {
    const v = view.current;
    if (v && value !== v.state.doc.toString()) v.dispatch({ changes: { from: 0, to: v.state.doc.length, insert: value } });
  }, [value]);

  return <div className="code-editor" ref={host} />;
}
