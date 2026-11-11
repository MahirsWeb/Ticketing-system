import { useEffect, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useLanguage } from '../i18n/LanguageContext';
import { useAuthStore } from '../store/authStore';
import type { UserRole } from '../types';

/** How long the loader is alone on the screen before it says why it is taking long and offers a way out. */
export const SLOW_RESTORE_MS = 5000;

/** The loader shown while the session is being restored from the cookie. A server that does not answer can keep it up for over a minute (four tries,
 *  each given 15 s), so after a few seconds it says so and offers the sign-in page instead of leaving the person looking at a bare "…". */
function RestoringSession() {
  const { t } = useLanguage();
  const finishInitializing = useAuthStore((s) => s.finishInitializing);
  const [slow, setSlow] = useState(false);
  useEffect(() => {
    const id = setTimeout(() => setSlow(true), SLOW_RESTORE_MS);
    return () => clearTimeout(id);
  }, []);
  return (
    <div role="status" className="py-16 text-center text-sm text-slate-600">
      …
      {slow && (
        <div className="mt-4 space-y-2">
          <p>{t('auth_sessionSlow')}</p>
          <button type="button" className="font-semibold text-slate-800 underline" onClick={finishInitializing}>
            {t('auth_goToSignIn')}
          </button>
        </div>
      )}
    </div>
  );
}

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const user = useAuthStore((s) => s.user);
  const token = useAuthStore((s) => s.accessToken);
  const initializing = useAuthStore((s) => s.initializing);
  const signedOutOnPurpose = useAuthStore((s) => s.signedOutOnPurpose);
  const location = useLocation();
  // The one-time "restore my session from the cookie" refresh is still running: wait for it rather than bounce to sign-in,
  // otherwise every reload would look like a sign-out for the moment the refresh takes.
  if (initializing) {
    return <RestoringSession />;
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
