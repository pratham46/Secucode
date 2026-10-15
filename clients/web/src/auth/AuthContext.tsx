import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { ApiError, type ApiClient } from '../api/client';
import { Session } from './session';

interface Auth {
  api: ApiClient;
  session: Session | null;
  // True after the server refused to refresh. The exam page stays mounted and asks for the password in a dialog.
  expired: boolean;
  login(username: string, password: string): Promise<void>;
  logout(): Promise<void>;
}

const AuthContext = createContext<Auth | null>(null);

export function useAuth(): Auth {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('useAuth must be used inside AuthProvider');
  return auth;
}

export function useSession(): Session {
  const session = useAuth().session;
  if (!session) throw new Error('No session: wrap the route in RequireAuth');
  return session;
}

export function AuthProvider({ api, children }: { api: ApiClient; children: ReactNode }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState<Session | null>(null);
  const [expired, setExpired] = useState(false);
  const [boot, setBoot] = useState<'loading' | 'ready' | ApiError>('loading');

  const restore = useCallback(() => {
    setBoot('loading');
    api.restore().then(
      me => {
        if (me) setSession(new Session(me));
        setBoot('ready');
      },
      (e: unknown) => setBoot(e instanceof ApiError ? e : ApiError.network()),
    );
  }, [api]);

  useEffect(() => {
    api.onSessionExpired = () => setExpired(true);
    restore();
  }, [api, restore]);

  const value = useMemo<Auth>(() => ({
    api,
    session,
    expired,
    async login(username, password) {
      const me = await api.login(username, password);
      setSession(new Session(me));
      setExpired(false);
    },
    async logout() {
      await api.logout();
      setSession(null);
      setExpired(false);
      queryClient.clear();
    },
  }), [api, session, expired, queryClient]);

  if (boot === 'loading') return <div className="boot" aria-busy="true">Loading…</div>;
  if (boot instanceof ApiError)
    return (
      <div className="boot" role="alert">
        <p><strong>{boot.title}</strong> {boot.detail}</p>
        <button type="button" className="btn" onClick={restore}>Retry</button>
      </div>
    );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function RequireAuth({ children, keepOnExpiry = false }: { children: ReactNode; keepOnExpiry?: boolean }) {
  const { session, expired } = useAuth();
  const location = useLocation();
  if (!session || (expired && !keepOnExpiry))
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search, expired }} />;
  return <>{children}</>;
}

export function StaffOnly({ children }: { children: ReactNode }) {
  return useSession().isStudent ? <Navigate to="/exams" replace /> : <>{children}</>;
}

export function StudentOnly({ children }: { children: ReactNode }) {
  return useSession().isStudent ? <>{children}</> : <Navigate to="/exams" replace />;
}
