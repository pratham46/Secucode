import { useState, type FormEvent } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { Button, ErrorBar, TextField } from '../../ui';
import { Dialog } from '../../ui/Dialog';

// Shown over the exam page when the session expires: the exam keeps running underneath and nothing is lost.
export function SignInDialog({ onSignedIn }: { onSignedIn(): void }) {
  const { login, session } = useAuth();
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!session) return;
    setBusy(true);
    setError(null);
    try {
      await login(session.user.username, password);
      onSignedIn();
    } catch (x) {
      setError(x);
      setBusy(false);
    }
  }

  return (
    <Dialog open title="Sign in to keep going" onClose={() => {}}
      actions={<Button type="submit" form="reauth" variant="primary" busy={busy}>Sign in</Button>}>
      <p>Your session expired. Your exam is still running and your work is kept in this tab.</p>
      {error !== null && <ErrorBar error={error} />}
      <form id="reauth" className="stack" onSubmit={submit}>
        <TextField label="Password" type="password" autoComplete="current-password" value={password}
          onChange={e => setPassword(e.target.value)} required autoFocus />
      </form>
    </Dialog>
  );
}
