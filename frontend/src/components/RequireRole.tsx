import { Navigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '../store/authStore';
import type { UserRole } from '../types';

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const user = useAuthStore((s) => s.user);
  const token = useAuthStore((s) => s.accessToken);
  const initializing = useAuthStore((s) => s.initializing);
  const signedOutOnPurpose = useAuthStore((s) => s.signedOutOnPurpose);
  const location = useLocation();
  // The one-time "restore my session from the cookie" refresh is still running: wait for it rather than bounce to sign-in,
  // otherwise every reload would look like a sign-out for the moment the refresh takes.
  if (initializing) {
    return <div role="status" className="py-16 text-center text-sm text-slate-600">…</div>;
  }
  // Remember where the person was going so the sign-in page can send them back (an expired session or a bookmarked link).
  // Not after "Sign out": whoever signs in next starts at the front page.
  if (!user || !token) {
    const state = signedOutOnPurpose ? undefined : { from: `${location.pathname}${location.search}` };
    return <Navigate to="/login" replace state={state} />;
  }
  return <>{children}</>;
}

export function RequireRole({ roles, children }: { roles: UserRole[]; children: React.ReactNode }) {
  const user = useAuthStore((s) => s.user);
  if (!user) return <Navigate to="/login" replace />;
  if (!roles.includes(user.role)) return <Navigate to="/" replace />;
  return <>{children}</>;
}
