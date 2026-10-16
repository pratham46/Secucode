import {
  useId,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';
import { ApiError } from '../api/client';

type Variant = 'primary' | 'standard' | 'subtle' | 'danger';

export function Button({ variant = 'standard', busy = false, className = '', disabled, children, ...rest }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button type="button" {...rest} className={`btn btn-${variant} ${className}`.trim()} disabled={disabled || busy} aria-busy={busy || undefined}>
      {busy && <Spinner size={14} />}
      {children}
    </button>
  );
}

function Field({ id, label, error, hint, children }: { id: string; label: string; error?: string; hint?: string; children: ReactNode }) {
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children}
      {error ? <small id={`${id}-err`} className="error">{error}</small> : hint && <small className="hint">{hint}</small>}
    </div>
  );
}

const fieldProps = (id: string, error?: string) => ({
  id,
  className: 'input',
  'aria-invalid': error ? true : undefined,
  'aria-describedby': error ? `${id}-err` : undefined,
});

export function TextField({ label, error, hint, id, ...rest }: InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string; hint?: string }) {
  const auto = useId();
  const fid = id ?? auto;
  return <Field id={fid} label={label} error={error} hint={hint}><input {...rest} {...fieldProps(fid, error)} /></Field>;
}

export function TextArea({ label, error, id, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement> & { label: string; error?: string }) {
  const auto = useId();
  const fid = id ?? auto;
  return <Field id={fid} label={label} error={error}><textarea {...rest} {...fieldProps(fid, error)} /></Field>;
}

export function Select({ label, error, id, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement> & { label: string; error?: string }) {
  const auto = useId();
  const fid = id ?? auto;
  return <Field id={fid} label={label} error={error}><select {...rest} {...fieldProps(fid, error)}>{children}</select></Field>;
}

interface CheckProps { label: string; checked: boolean; onChange(value: boolean): void; disabled?: boolean }

export function Checkbox({ label, checked, onChange, disabled }: CheckProps) {
  return (
    <label className="check">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={e => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

export function Toggle({ label, checked, onChange, disabled }: CheckProps) {
  return (
    <label className="toggle">
      <input type="checkbox" role="switch" checked={checked} disabled={disabled} onChange={e => onChange(e.target.checked)} />
      <span className="track" aria-hidden="true" />
      <span>{label}</span>
    </label>
  );
}

export function Card({ children, className = '', as: Tag = 'section' }: { children: ReactNode; className?: string; as?: 'section' | 'article' | 'div' }) {
  return <Tag className={`card ${className}`.trim()}>{children}</Tag>;
}

export function Spinner({ size = 20 }: { size?: number }) {
  return <span className="spinner" role="progressbar" aria-label="Loading" style={{ width: size, height: size }} />;
}

export function Badge({ tone = 'neutral', children }: { tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger'; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

export type InfoKind = 'info' | 'success' | 'warning' | 'error';

export function InfoBar({ kind = 'info', title, children, action }: { kind?: InfoKind; title: string; children?: ReactNode; action?: ReactNode }) {
  const urgent = kind === 'error' || kind === 'warning';
  return (
    <div className={`infobar infobar-${kind}`} role={urgent ? 'alert' : 'status'}>
      <div><strong>{title}</strong>{children && <> {children}</>}</div>
      {action}
    </div>
  );
}

export function ErrorBar({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const e = error instanceof ApiError ? error : new ApiError(-1, 'Something went wrong.', error instanceof Error ? error.message : undefined);
  return (
    <InfoBar kind="error" title={e.title} action={e.status === 0 && onRetry ? <Button variant="subtle" onClick={onRetry}>Retry</Button> : undefined}>
      {e.detail}
    </InfoBar>
  );
}

export interface Column<T> { header: string; cell(row: T): ReactNode; width?: string }

export function DataTable<T>({ rows, columns, rowKey, empty, caption }:
  { rows: T[]; columns: Column<T>[]; rowKey(row: T): string | number; empty: string; caption: string }) {
  if (rows.length === 0) return <p className="empty">{empty}</p>;
  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>{columns.map(c => <th key={c.header} scope="col" style={{ width: c.width }}>{c.header}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map(r => <tr key={rowKey(r)}>{columns.map(c => <td key={c.header}>{c.cell(r)}</td>)}</tr>)}
        </tbody>
      </table>
    </div>
  );
}
