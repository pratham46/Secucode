import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { Button, ErrorBar, InfoBar, TextField } from '../../ui';

export function LoginPage() {
  const { login, session, expired } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const state = location.state as { from?: string; expired?: boolean } | null;
  const from = state?.from && state.from !== '/login' ? state.from : '/exams';
  const [username, setUsername] = useState(session?.user.username ?? '');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  if (session && !expired) return <Navigate to={from} replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(username.trim(), password);
      navigate(from, { replace: true });
    } catch (x) {
      setError(x);
      setBusy(false);
    }
  }

  return (
    <div className="login-page">
      <form className="card login-card" onSubmit={submit}>
        <div>
          <h1 className="title">SecuCode</h1>
          <p className="secondary">Sign in to continue.</p>
        </div>
        {(state?.expired || expired) && error === null && <InfoBar kind="warning" title="Your session expired.">Please sign in again.</InfoBar>}
        {error !== null && <ErrorBar error={error} />}
        <TextField label="Username" autoComplete="username" value={username} onChange={e => setUsername(e.target.value)} required autoFocus />
        <TextField label="Password" type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required />
        <Button type="submit" variant="primary" busy={busy}>Sign in</Button>
      </form>
    </div>
  );
}
