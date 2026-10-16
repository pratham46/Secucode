import { useEffect, useId, useRef, type ReactNode } from 'react';

// The native <dialog> gives a modal with focus containment, Escape and a backdrop for free.
export function Dialog({ open, title, children, actions, onClose }:
  { open: boolean; title: string; children?: ReactNode; actions: ReactNode; onClose(): void }) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();

  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal?.();
    if (!open && d.open) d.close();
  }, [open]);

  return (
    <dialog ref={ref} className="dialog" aria-labelledby={titleId} onCancel={e => { e.preventDefault(); onClose(); }}>
      {open && (
        <>
          <h2 id={titleId}>{title}</h2>
          {children && <div className="dialog-body">{children}</div>}
          <div className="dialog-actions">{actions}</div>
        </>
      )}
    </dialog>
  );
}
