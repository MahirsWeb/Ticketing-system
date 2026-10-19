import { lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import LoginPage from './pages/auth/LoginPage';
import SetNewPasswordPage from './pages/auth/SetNewPasswordPage';
import VerifyEmailPage from './pages/auth/VerifyEmailPage';
import ForgotPasswordPage from './pages/auth/ForgotPasswordPage';
import ResetPasswordPage from './pages/auth/ResetPasswordPage';
import AppLayout from './layouts/AppLayout';
import { RequireAuth, RequireRole } from './components/RequireRole';
import NotFoundPage from './pages/NotFoundPage';
import { useAuthStore } from './store/authStore';
import { useLanguage } from './i18n/LanguageContext';

// Each screen is its own chunk, loaded when it is first opened. The charts and the rich-text editor alone are most of the
// download, and a client who only files tickets never needs the dashboard.
const DashboardPage = lazy(() => import('./pages/dashboard/DashboardPage'));
const TicketsListPage = lazy(() => import('./pages/tickets/TicketsListPage'));
const TicketDetailPage = lazy(() => import('./pages/tickets/TicketDetailPage'));
const NewTicketPage = lazy(() => import('./pages/tickets/NewTicketPage'));
const NewTicketOnBehalfPage = lazy(() => import('./pages/tickets/NewTicketOnBehalfPage'));
const UsersPage = lazy(() => import('./pages/users/UsersPage'));
const UserDetailPage = lazy(() => import('./pages/users/UserDetailPage'));
const CompanyDetailPage = lazy(() => import('./pages/users/CompanyDetailPage'));
const TasksPage = lazy(() => import('./pages/tasks/TasksPage'));
const SettingsPage = lazy(() => import('./pages/settings/SettingsPage'));
const EmailsPage = lazy(() => import('./pages/emails/EmailsPage'));
const EmailCallbackPage = lazy(() => import('./pages/emails/EmailCallbackPage'));
const AiAssistantPage = lazy(() => import('./pages/knowledgeBase/AiAssistantPage'));
const SimilarTicketsPage = lazy(() => import('./pages/tickets/SimilarTicketsPage'));

const queryClient = new QueryClient();

function HomeRoute() {
  const user = useAuthStore((s) => s.user);
  if (user?.role === 'Client') return <Navigate to="/tickets" replace />;
  return <DashboardPage />;
}

function PageFallback() {
  const { t } = useLanguage();
  return (
    <div role="status" className="py-16 text-center text-sm text-slate-500">
      {t('common_loading')}
    </div>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Suspense fallback={<PageFallback />}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/set-new-password" element={<SetNewPasswordPage />} />
            <Route path="/verify-email" element={<VerifyEmailPage />} />
            <Route path="/forgot-password" element={<ForgotPasswordPage />} />
            <Route path="/reset-password" element={<ResetPasswordPage />} />
            <Route
              path="/emails/callback"
              element={
                <RequireAuth>
                  <EmailCallbackPage />
                </RequireAuth>
              }
            />

            <Route
              element={
                <RequireAuth>
                  <AppLayout />
                </RequireAuth>
              }
            >
              <Route path="/" element={<HomeRoute />} />
              <Route path="/tickets" element={<TicketsListPage />} />
              <Route path="/tickets/:id" element={<TicketDetailPage />} />
              <Route
                path="/tickets/new"
                element={
                  <RequireRole roles={['Client']}>
                    <NewTicketPage />
                  </RequireRole>
                }
              />
              <Route
                path="/tasks"
                element={
                  <RequireRole roles={['Admin', 'Employee']}>
                    <TasksPage />
                  </RequireRole>
                }
              />
              <Route
                path="/users"
                element={
                  <RequireRole roles={['Admin', 'Employee']}>
                    <UsersPage />
                  </RequireRole>
                }
              />
              <Route
                path="/users/:id"
                element={
                  <RequireRole roles={['Admin', 'Employee']}>
                    <UserDetailPage />
                  </RequireRole>
                }
              />
              <Route
                path="/companies/:id"
                element={
                  <RequireRole roles={['Admin', 'Employee']}>
                    <CompanyDetailPage />
                  </RequireRole>
                }
              />
              <Route
                path="/tickets/new-on-behalf"
                element={
                  <RequireRole roles={['Admin', 'Employee']}>
                    <NewTicketOnBehalfPage />
                  </RequireRole>
                }
              />
              <Route
                path="/ai-assistant"
                element={
                  <RequireRole roles={['Admin', 'Employee']}>
                    <AiAssistantPage />
                  </RequireRole>
                }
              />
              <Route
                path="/knowledge-base/similar"
                element={
                  <RequireRole roles={['Admin', 'Employee']}>
                    <SimilarTicketsPage />
                  </RequireRole>
                }
              />
              <Route
                path="/settings"
                element={
                  <RequireRole roles={['Admin']}>
                    <SettingsPage />
                  </RequireRole>
                }
              />
              <Route
                path="/emails"
                element={
                  <RequireRole roles={['Employee']}>
                    <EmailsPage />
                  </RequireRole>
                }
              />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
        </Routes>
        </Suspense>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
