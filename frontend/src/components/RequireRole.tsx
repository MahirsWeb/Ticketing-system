import { Navigate } from 'react-router-dom';
import { useAuthStore } from '../store/authStore';
import type { UserRole } from '../types';

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const user = useAuthStore((s) => s.user);
  const token = useAuthStore((s) => s.accessToken);
  if (!user || !token) return <Navigate to="/login" replace />;
  return <>{children}</>;
}

export function RequireRole({ roles, children }: { roles: UserRole[]; children: React.ReactNode }) {
  const user = useAuthStore((s) => s.user);
  if (!user) return <Navigate to="/login" replace />;
  if (!roles.includes(user.role)) return <Navigate to="/" replace />;
  return <>{children}</>;
}
