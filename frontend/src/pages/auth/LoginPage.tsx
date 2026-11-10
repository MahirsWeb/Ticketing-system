import { useState } from 'react';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { useAuthStore } from '../../store/authStore';
import { safeReturnPath } from '../../lib/returnPath';
import { Button, ErrorText, IconInput } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';
import { useLanguage } from '../../i18n/LanguageContext';

function UserIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
      <circle cx="12" cy="7" r="4" />
    </svg>
  );
}

function LockIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="11" width="18" height="11" rx="2" />
      <path d="M7 11V7a5 5 0 0 1 10 0v4" />
    </svg>
  );
}

export default function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const setSession = useAuthStore((s) => s.setSession);
  const sessionExpired = useAuthStore((s) => s.sessionExpired);
  const { t } = useLanguage();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await authApi.login(email, password);
      if (res.requiresPasswordChange) {
        navigate('/set-new-password', { state: { email, temporaryPassword: password } });
        return;
      }
      if (res.accessToken && res.user) {
        setSession(res.accessToken, res.user);
        navigate(safeReturnPath((location.state as { from?: unknown } | null)?.from), { replace: true });
      }
    } catch (err: any) {
      const code = err?.response?.data?.code;
      if (code === 'EMAIL_NOT_VERIFIED') {
        setError(t('login_errEmailNotVerified'));
      } else if (code === 'TEMP_PASSWORD_EXPIRED') {
        setError(t('login_errTempPasswordExpired'));
      } else {
        // A locked account gets the same answer as a wrong password (the server no longer distinguishes them), so there
        // is deliberately no "account locked" message here.
        setError(err?.response?.data?.message ?? t('login_errGeneric'));
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthSplitLayout>
      <div className="w-full max-w-sm">
        <div className="mb-8 lg:hidden">
          <span className="text-lg font-bold text-[#1a2b4c]">{t('appName')}</span>
        </div>
        <h1 className="mb-1 text-2xl font-bold text-slate-900">{t('login_welcomeBack')}</h1>
        <p className="mb-8 text-sm text-slate-600">{t('login_subtitle')}</p>

        {sessionExpired && (
          <p role="status" className="mb-4 rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
            {t('login_sessionExpired')}
          </p>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <IconInput
            icon={<UserIcon />}
            type="email"
            aria-label={t('login_emailLabel')}
            placeholder={t('login_emailPlaceholder')}
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoFocus
          />
          <IconInput
            icon={<LockIcon />}
            type="password"
            aria-label={t('login_passwordLabel')}
            placeholder={t('login_passwordPlaceholder')}
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />

          <div className="text-right">
            <Link to="/forgot-password" className="text-xs font-medium text-blue-700 hover:underline">
              {t('login_forgotPassword')}
            </Link>
          </div>

          <ErrorText>{error}</ErrorText>

          <Button type="submit" variant="pill" className="w-full" disabled={loading}>
            {loading ? t('login_signingIn') : t('login_signIn')}
          </Button>
        </form>
      </div>
    </AuthSplitLayout>
  );
}
