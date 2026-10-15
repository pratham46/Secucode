import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { createBrowserRouter, Navigate, RouterProvider, type RouteObject } from 'react-router-dom';
import { ApiClient, ApiError } from './api/client';
import { AuthProvider, RequireAuth } from './auth/AuthContext';
import { ExamsHome } from './features/exams/ExamsHome';
import { LoginPage } from './features/login/LoginPage';
import { SettingsPage } from './features/settings/SettingsPage';
import { AppShell } from './shell/AppShell';

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
  // Full-screen routes without the shell (the student exam page, Task 13) go here.
  {
    element: <RequireAuth><AppShell /></RequireAuth>,
    children: [
      { path: '/', element: <Navigate to="/exams" replace /> },
      { path: '/exams', element: <ExamsHome /> },
      // Staff routes (Tasks 7–8) and student routes (Task 12) go here.
      { path: '/settings', element: <SettingsPage /> },
      { path: '*', element: <Navigate to="/exams" replace /> },
    ],
  },
];

const api = new ApiClient();
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      // Retry connection and server failures, not refusals (4xx).
      retry: (count, error) => count < 2 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
    },
  },
});

export function App() {
  const [router] = useState(() => createBrowserRouter(routes));
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider api={api}>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  );
}
