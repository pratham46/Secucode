import type { LoginResponse, MeResponse, ProblemDetails } from './types';

// Every failure the app sees is an ApiError: a server problem (status = HTTP status) or no connection (status 0).
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail?: string,
    readonly errors?: Record<string, string[]>,
  ) {
    super(detail ?? title);
    this.name = 'ApiError';
  }

  static network(): ApiError {
    return new ApiError(0, "Can't reach the server", 'Check your connection and try again.');
  }

  static async from(res: Response): Promise<ApiError> {
    let p: ProblemDetails = {};
    try {
      p = (await res.json()) as ProblemDetails;
    } catch {
      /* not JSON */
    }
    return new ApiError(res.status, p.title || res.statusText || `Error ${res.status}`, p.detail ?? undefined, p.errors);
  }
}

export interface TokenStore {
  get(): string | null;
  set(token: string | null): void;
}

const REFRESH_KEY = 'secucode.refresh';

// Survives a reload, cleared when the tab closes. Readable by page scripts, so the API serves the app with a strict CSP.
export const sessionTokenStore: TokenStore = {
  get: () => {
    try {
      return sessionStorage.getItem(REFRESH_KEY);
    } catch {
      return null;
    }
  },
  set: token => {
    try {
      if (token) sessionStorage.setItem(REFRESH_KEY, token);
      else sessionStorage.removeItem(REFRESH_KEY);
    } catch {
      /* storage blocked: the session lasts until reload */
    }
  },
};

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

// The app's only door to the server. The access token lives in memory; concurrent 401s share one refresh.
export class ApiClient {
  private accessToken: string | null = null;
  private expiresAt = 0;
  private refreshing: Promise<boolean> | null = null;
  private restoring: Promise<MeResponse | null> | null = null;

  // Called once when the server refuses to refresh: the user must sign in again.
  onSessionExpired: () => void = () => {};

  constructor(
    private readonly fetchFn: typeof fetch = (input, init) => fetch(input, init),
    private readonly refreshStore: TokenStore = sessionTokenStore,
  ) {}

  get isSignedIn(): boolean {
    return this.accessToken !== null;
  }

  async login(username: string, password: string): Promise<MeResponse> {
    this.setTokens(await this.request<LoginResponse>('POST', '/api/auth/login', { username, password, client: 'Web' }, false));
    return this.get<MeResponse>('/api/auth/me');
  }

  // Page load: turn a stored refresh token back into a session. Null means "sign in". Idempotent (StrictMode runs effects twice).
  restore(): Promise<MeResponse | null> {
    return (this.restoring ??= this.doRestore().finally(() => { this.restoring = null; }));
  }

  private async doRestore(): Promise<MeResponse | null> {
    if (this.refreshStore.get() === null) return null;
    if (!(await this.refresh(null))) {
      this.clear();
      return null;
    }
    return this.get<MeResponse>('/api/auth/me');
  }

  async logout(): Promise<void> {
    const token = this.refreshStore.get();
    try {
      if (token) await this.request('POST', '/api/auth/logout', { refreshToken: token }, true, false);
    } catch {
      /* signing out locally matters more than telling the server */
    } finally {
      this.clear();
    }
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path);
  }
  post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body);
  }
  put<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>('PUT', path, body);
  }
  patch<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>('PATCH', path, body);
  }
  del<T>(path: string): Promise<T> {
    return this.request<T>('DELETE', path);
  }

  // For SignalR: a token valid for at least another minute. Offline, the old token is returned and the hub retries.
  async freshAccessToken(): Promise<string> {
    const token = this.accessToken;
    if (token && this.expiresAt - Date.now() < 60_000) {
      try {
        await this.refresh(token);
      } catch {
        /* offline: try with what we have */
      }
    }
    return this.accessToken ?? '';
  }

  private async request<T>(method: Method, path: string, body?: unknown, authenticated = true, signalExpiry = true): Promise<T> {
    const res = await this.send(method, path, body, authenticated, signalExpiry);
    if (res.status === 204) return undefined as T;
    const type = res.headers.get('content-type') ?? '';
    return (type.includes('json') ? await res.json() : await res.blob()) as T;
  }

  private async send(method: Method, path: string, body: unknown, authenticated: boolean, signalExpiry: boolean): Promise<Response> {
    const used = this.accessToken;
    let res = await this.sendOnce(method, path, body, authenticated);
    if (res.status === 401 && authenticated && used !== null) {
      if (!(await this.refresh(used))) {
        if (this.clear() && signalExpiry) this.onSessionExpired();
        throw new ApiError(401, 'Session expired', 'Your session expired. Please sign in again.');
      }
      res = await this.sendOnce(method, path, body, authenticated);
    }
    if (!res.ok) throw await ApiError.from(res);
    return res;
  }

  private async sendOnce(method: Method, path: string, body: unknown, authenticated: boolean): Promise<Response> {
    const headers: Record<string, string> = {};
    if (authenticated && this.accessToken) headers.Authorization = `Bearer ${this.accessToken}`;
    let payload: BodyInit | undefined;
    if (body instanceof FormData) payload = body;
    else if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    try {
      return await this.fetchFn(path, { method, headers, body: payload });
    } catch {
      throw ApiError.network();
    }
  }

  // A caller whose token was already replaced just retries with the new one.
  private refresh(stale: string | null): Promise<boolean> {
    if (this.accessToken !== stale) return Promise.resolve(this.accessToken !== null);
    const token = this.refreshStore.get();
    if (!token) return Promise.resolve(false);
    return (this.refreshing ??= this.doRefresh(token).finally(() => { this.refreshing = null; }));
  }

  private async doRefresh(token: string): Promise<boolean> {
    const res = await this.sendOnce('POST', '/api/auth/refresh', { refreshToken: token }, false);
    if (res.status === 400 || res.status === 401 || res.status === 403) return false;
    if (!res.ok) throw await ApiError.from(res); // 5xx, 429...: not a refusal, keep the session
    const login = (await res.json()) as LoginResponse;
    if (this.refreshStore.get() !== token) return false; // signed out meanwhile
    this.setTokens(login);
    return true;
  }

  private setTokens(r: LoginResponse): void {
    this.accessToken = r.accessToken;
    this.expiresAt = Date.parse(r.expiresAt);
    this.refreshStore.set(r.refreshToken);
  }

  // True only for the call that actually ended a signed-in session.
  private clear(): boolean {
    const was = this.accessToken !== null;
    this.accessToken = null;
    this.refreshStore.set(null);
    return was;
  }
}
