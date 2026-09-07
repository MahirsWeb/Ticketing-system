import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import LoginPage from './pages/auth/LoginPage';
import SetNewPasswordPage from './pages/auth/SetNewPasswordPage';
import VerifyEmailPage from './pages/auth/VerifyEmailPage';
import ForgotPasswordPage from './pages/auth/ForgotPasswordPage';
import ResetPasswordPage from './pages/auth/ResetPasswordPage';
import AppLayout from './layouts/AppLayout';
import { RequireAuth, RequireRole } from './components/RequireRole';
import DashboardPage from './pages/dashboard/DashboardPage';
import TicketsListPage from './pages/tickets/TicketsListPage';
import TicketDetailPage from './pages/tickets/TicketDetailPage';
import NewTicketPage from './pages/tickets/NewTicketPage';
import NewTicketOnBehalfPage from './pages/tickets/NewTicketOnBehalfPage';
import UsersPage from './pages/users/UsersPage';
import UserDetailPage from './pages/users/UserDetailPage';
import CompanyDetailPage from './pages/users/CompanyDetailPage';
import TasksPage from './pages/tasks/TasksPage';
import SettingsPage from './pages/settings/SettingsPage';
import EmailsPage from './pages/emails/EmailsPage';
import EmailCallbackPage from './pages/emails/EmailCallbackPage';
import AiAssistantPage from './pages/knowledgeBase/AiAssistantPage';
import SimilarTicketsPage from './pages/tickets/SimilarTicketsPage';
import { useAuthStore } from './store/authStore';

const queryClient = new QueryClient();

function HomeRoute() {
  const user = useAuthStore((s) => s.user);
  if (user?.role === 'Client') return <Navigate to="/tickets" replace />;
  return <DashboardPage />;
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
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
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
