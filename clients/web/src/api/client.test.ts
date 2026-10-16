import { describe, expect, it, vi } from 'vitest';
import { ApiClient, ApiError, type TokenStore } from './client';

function memoryStore(initial: string | null = null): TokenStore {
  let v = initial;
  return { get: () => v, set: t => { v = t; } };
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });

const login = (n: number) => ({
  accessToken: `a${n}`, refreshToken: `r${n}`, expiresAt: new Date(Date.now() + 900_000).toISOString(), user: {},
});
const me = { user: { id: 1, username: 'u', fullName: 'U', email: null, isActive: true, hasPin: false, assignments: [] }, capabilities: [] };

type Call = { method: string; path: string; auth: string | null; body: unknown };

function server(route: (c: Call) => Response | Promise<Response>) {
  const calls: Call[] = [];
  const fetchFn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const headers = new Headers(init?.headers);
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body;
    const c: Call = { method: init?.method ?? 'GET', path: String(input), auth: headers.get('Authorization'), body };
    calls.push(c);
    return route(c);
  });
  return { calls, fetchFn: fetchFn as unknown as typeof fetch };
}

async function signedIn(route: (c: Call) => Response | Promise<Response>, store = memoryStore()) {
  const s = server(c => (c.path === '/api/auth/login' ? json(login(1)) : c.path === '/api/auth/me' ? json(me) : route(c)));
  const api = new ApiClient(s.fetchFn, store);
  await api.login('u', 'p');
  s.calls.length = 0;
  return { api, calls: s.calls, store };
}

describe('ApiClient', () => {
  it('signs in as the Web client and keeps the refresh token in the store', async () => {
    const store = memoryStore();
    const s = server(c => (c.path === '/api/auth/login' ? json(login(1)) : json(me)));
    const api = new ApiClient(s.fetchFn, store);

    expect(await api.login('u', 'p')).toEqual(me);

    expect(s.calls[0]).toMatchObject({ method: 'POST', body: { username: 'u', password: 'p', client: 'Web' }, auth: null });
    expect(s.calls[1]).toMatchObject({ path: '/api/auth/me', auth: 'Bearer a1' });
    expect(store.get()).toBe('r1');
    expect(api.isSignedIn).toBe(true);
  });

  it('refreshes once on 401 and retries with the new token', async () => {
    const { api, calls } = await signedIn(c =>
      c.path === '/api/auth/refresh' ? json(login(2)) : c.auth === 'Bearer a2' ? json({ ok: true }) : json({ title: 'Unauthorized' }, 401));

    expect(await api.get('/api/exams')).toEqual({ ok: true });

    expect(calls.map(c => `${c.path} ${c.auth}`)).toEqual(['/api/exams Bearer a1', '/api/auth/refresh null', '/api/exams Bearer a2']);
    expect(calls[1].body).toEqual({ refreshToken: 'r1' });
  });

  it('shares one refresh between concurrent 401s', async () => {
    const { api, calls } = await signedIn(c =>
      c.path === '/api/auth/refresh' ? json(login(2)) : c.auth === 'Bearer a2' ? json(1) : json({}, 401));

    await Promise.all([api.get('/api/a'), api.get('/api/b'), api.get('/api/c')]);

    expect(calls.filter(c => c.path === '/api/auth/refresh')).toHaveLength(1);
  });

  it('ends the session once when the server refuses the refresh', async () => {
    const { api, store } = await signedIn(c => (c.path === '/api/auth/refresh' ? json({ title: 'Bad' }, 401) : json({}, 401)));
    const expired = vi.fn();
    api.onSessionExpired = expired;

    await Promise.all([
      expect(api.get('/api/a')).rejects.toMatchObject({ status: 401 }),
      expect(api.get('/api/b')).rejects.toMatchObject({ status: 401 }),
    ]);

    expect(expired).toHaveBeenCalledOnce();
    expect(api.isSignedIn).toBe(false);
    expect(store.get()).toBeNull();
  });

  it('keeps the session when the refresh fails for another reason', async () => {
    const { api, store } = await signedIn(c => (c.path === '/api/auth/refresh' ? json({ title: 'Down' }, 503) : json({}, 401)));
    const expired = vi.fn();
    api.onSessionExpired = expired;

    await expect(api.get('/api/exams')).rejects.toMatchObject({ status: 503 });

    expect(expired).not.toHaveBeenCalled();
    expect(api.isSignedIn).toBe(true);
    expect(store.get()).toBe('r1');
  });

  it('turns problem+json into ApiError', async () => {
    const { api } = await signedIn(() => json({ title: 'Forbidden', detail: 'Incorrect PIN.', status: 403 }, 403));

    const e = await api.post('/api/exams/1/attempts', { pin: '0' }).catch((x: unknown) => x);

    expect(e).toBeInstanceOf(ApiError);
    expect(e).toMatchObject({ status: 403, title: 'Forbidden', detail: 'Incorrect PIN.' });
  });

  it('reports a network failure as status 0', async () => {
    const { api } = await signedIn(() => { throw new TypeError('Failed to fetch'); });

    await expect(api.get('/api/exams')).rejects.toMatchObject({ status: 0, title: "Can't reach the server" });
  });

  it('returns undefined for 204 and a Blob for non-JSON bodies', async () => {
    const { api } = await signedIn(c =>
      c.path === '/api/x' ? new Response(null, { status: 204 }) : new Response('img', { headers: { 'content-type': 'image/jpeg' } }));

    expect(await api.put('/api/x', {})).toBeUndefined();
    const blob = await api.get<Blob>('/api/evidence/1');
    expect(blob).toBeInstanceOf(Blob);
    expect(blob.size).toBe(3);
  });

  it('restores a session from a stored refresh token', async () => {
    const store = memoryStore('r1');
    const s = server(c => (c.path === '/api/auth/refresh' ? json(login(2)) : json(me)));
    const api = new ApiClient(s.fetchFn, store);

    expect(await api.restore()).toEqual(me);

    expect(s.calls.map(c => c.path)).toEqual(['/api/auth/refresh', '/api/auth/me']);
    expect(store.get()).toBe('r2');
  });

  it('restores nothing without a stored token, and clears a refused one', async () => {
    expect(await new ApiClient(server(() => json({})).fetchFn, memoryStore()).restore()).toBeNull();

    const store = memoryStore('old');
    expect(await new ApiClient(server(() => json({}, 401)).fetchFn, store).restore()).toBeNull();
    expect(store.get()).toBeNull();
  });

  it('logout tells the server and always clears locally', async () => {
    const { api, calls, store } = await signedIn(() => { throw new TypeError('offline'); });

    await api.logout();

    expect(calls[0]).toMatchObject({ path: '/api/auth/logout', body: { refreshToken: 'r1' } });
    expect(api.isSignedIn).toBe(false);
    expect(store.get()).toBeNull();
  });
});
