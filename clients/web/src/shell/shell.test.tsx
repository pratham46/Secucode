import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { ApiClient, type TokenStore } from '../api/client';
import { routes } from '../App';
import { AuthProvider } from '../auth/AuthContext';

const store = (v: string | null): TokenStore => ({ get: () => v, set: () => {} });
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
const me = {
  user: { id: 1, username: 'hod', fullName: 'Dr. Asha', email: null, isActive: true, hasPin: false,
    assignments: [{ id: 1, roleId: 2, roleName: 'HOD', branchId: 1, classId: null, batchId: null, validFrom: null, validTo: null, isActive: true, isStudent: false }] },
  capabilities: [],
};

function renderAt(path: string, refreshToken: string | null) {
  const api = new ApiClient(async input => (String(input) === '/api/auth/refresh'
    ? json({ accessToken: 'a', refreshToken: 'r', expiresAt: new Date(Date.now() + 9e5).toISOString(), user: me.user })
    : json(me)), store(refreshToken));
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(<QueryClientProvider client={new QueryClient()}><AuthProvider api={api}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>);
  return router;
}

describe('shell', () => {
  it('sends a signed-out visitor to sign-in, remembering where they were going', async () => {
    const router = renderAt('/settings', null);
    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeTruthy();
    expect(router.state.location.pathname).toBe('/login');
    expect(router.state.location.state).toMatchObject({ from: '/settings' });
  });

  it('restores a session and shows the sidebar with the current page marked', async () => {
    renderAt('/settings', 'r0');
    expect(await screen.findByRole('heading', { name: 'Settings' })).toBeTruthy();
    expect(screen.getByRole('navigation', { name: 'Main' }).textContent).toContain('Exams');
    expect(screen.getByRole('link', { name: 'Settings' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getAllByText('Dr. Asha').length).toBeGreaterThan(0);
  });
});
