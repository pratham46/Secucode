import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import { Button, DataTable, ErrorBar, InfoBar, TextField } from './index';

describe('ui', () => {
  it('announces errors and warnings as alerts, other InfoBars as status', () => {
    render(<><InfoBar kind="error" title="Broken" /><InfoBar title="Note" /></>);
    expect(screen.getByRole('alert').textContent).toContain('Broken');
    expect(screen.getByRole('status').textContent).toContain('Note');
  });

  it('offers Retry only for connection failures', () => {
    const retry = vi.fn();
    const { rerender } = render(<ErrorBar error={ApiError.network()} onRetry={retry} />);
    screen.getByRole('button', { name: 'Retry' }).click();
    expect(retry).toHaveBeenCalledOnce();

    rerender(<ErrorBar error={new ApiError(403, 'Forbidden', 'Not yours.')} onRetry={retry} />);
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(screen.getByRole('alert').textContent).toContain('Not yours.');
  });

  it('shows unexpected errors with a generic title', () => {
    render(<ErrorBar error={new Error('boom')} />);
    expect(screen.getByRole('alert').textContent).toContain('Something went wrong');
  });

  it('disables a busy button and defaults to type=button', () => {
    render(<Button busy>Save</Button>);
    const b = screen.getByRole('button', { name: /Save/ }) as HTMLButtonElement;
    expect(b.disabled).toBe(true);
    expect(b.type).toBe('button');
  });

  it('labels fields and links their error', () => {
    render(<TextField label="Title" error="Enter a title." value="" onChange={() => {}} />);
    const input = screen.getByLabelText('Title');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(document.getElementById(input.getAttribute('aria-describedby')!)?.textContent).toBe('Enter a title.');
  });

  it('renders rows, or the empty text', () => {
    const columns = [{ header: 'Name', cell: (r: { n: string }) => r.n }];
    const { rerender } = render(<DataTable caption="People" rows={[{ n: 'Asha' }]} columns={columns} rowKey={r => r.n} empty="Nobody." />);
    expect(screen.getByRole('table', { name: 'People' }).textContent).toContain('Asha');
    rerender(<DataTable caption="People" rows={[]} columns={columns} rowKey={r => r.n} empty="Nobody." />);
    expect(screen.getByText('Nobody.')).toBeTruthy();
  });
});
