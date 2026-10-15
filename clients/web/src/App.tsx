import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { createBrowserRouter, Navigate, RouterProvider, type RouteObject } from 'react-router-dom';
import { ApiClient, ApiError } from './api/client';
import { AuthProvider, RequireAuth, StaffOnly, StudentOnly } from './auth/AuthContext';
import { ExamsHome } from './features/exams/ExamsHome';
import { AttemptDetailPage } from './features/staff/AttemptDetailPage';
import { AttemptListPage } from './features/staff/AttemptListPage';
import { ExamEditorPage } from './features/staff/ExamEditorPage';
import { LoginPage } from './features/login/LoginPage';
import { ExamPage } from './features/student/ExamPage';
import { ExamStartPage } from './features/student/ExamStartPage';
import { SettingsPage } from './features/settings/SettingsPage';
import { AppShell } from './shell/AppShell';

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
  // Full-screen routes without the shell.
  { path: '/attempt/:id', element: <RequireAuth keepOnExpiry><StudentOnly><ExamPage /></StudentOnly></RequireAuth> },
  {
    element: <RequireAuth><AppShell /></RequireAuth>,
    children: [
      { path: '/', element: <Navigate to="/exams" replace /> },
      { path: '/exams', element: <ExamsHome /> },
      { path: '/exams/new', element: <StaffOnly><ExamEditorPage /></StaffOnly> },
      { path: '/exams/:id/edit', element: <StaffOnly><ExamEditorPage /></StaffOnly> },
      { path: '/exams/:id/attempts', element: <StaffOnly><AttemptListPage /></StaffOnly> },
      { path: '/attempts/:id', element: <StaffOnly><AttemptDetailPage /></StaffOnly> },
      { path: '/exams/:id/start', element: <StudentOnly><ExamStartPage /></StudentOnly> },
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
