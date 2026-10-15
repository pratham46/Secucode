import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth, useSession } from '../../auth/AuthContext';
import { applyTheme, loadTheme, type ThemeChoice } from '../../theme/theme';
import { Button, Card } from '../../ui';

const CHOICES: { value: ThemeChoice; label: string }[] = [
  { value: 'system', label: 'Use system setting' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
];

export function SettingsPage() {
  const { logout } = useAuth();
  const session = useSession();
  const navigate = useNavigate();
  const [theme, setTheme] = useState(loadTheme);
  const [busy, setBusy] = useState(false);

  function choose(choice: ThemeChoice) {
    setTheme(choice);
    applyTheme(choice);
  }

  async function signOut() {
    setBusy(true);
    await logout();
    navigate('/login', { replace: true });
  }

  return (
    <>
      <h1 className="title">Settings</h1>
      <Card className="stack">
        <h2 className="subtitle">Appearance</h2>
        <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0, gap: 8 }}>
          <legend className="sr-only">Theme</legend>
          {CHOICES.map(c => (
            <label key={c.value} className="check">
              <input type="radio" name="theme" value={c.value} checked={theme === c.value} onChange={() => choose(c.value)} />
              <span>{c.label}</span>
            </label>
          ))}
        </fieldset>
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Account</h2>
        <p>{session.user.fullName} <span className="secondary">({session.user.username}{session.roleNames && ` · ${session.roleNames}`})</span></p>
        <div><Button onClick={signOut} busy={busy}>Sign out</Button></div>
      </Card>
    </>
  );
}
