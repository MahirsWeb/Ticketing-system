import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Card, Spinner } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';
import { useLanguage } from '../../i18n/LanguageContext';

export default function VerifyEmailPage() {
  const { t } = useLanguage();
  const [params] = useSearchParams();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState('');

  useEffect(() => {
    const userId = params.get('userId');
    const token = params.get('token');
    if (!userId || !token) {
      setStatus('error');
      setMessage(t('auth_verify_missing'));
      return;
    }
    authApi
      .verifyEmail(userId, token)
      .then((res) => {
        setStatus('success');
        setMessage(res.message ?? t('auth_verify_ok'));
      })
      .catch((err) => {
        setStatus('error');
        setMessage(err?.response?.data?.message ?? t('auth_verify_invalid'));
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the link is verified once when the page opens; its token comes from the address and does not change
  }, []);

  return (
    <AuthSplitLayout>
      <Card className="w-full max-w-sm p-8 text-center">
        <h1 className="mb-4 text-xl font-bold text-slate-900">{t('auth_verify_title')}</h1>
        {status === 'loading' && <Spinner className="mx-auto h-8 w-8 text-blue-700" />}
        {status !== 'loading' && (
          <p className={status === 'success' ? 'text-green-700' : 'text-red-700'}>{message}</p>
        )}
        <Link to="/login" className="mt-6 inline-block text-sm text-blue-700 hover:underline">
          {t('auth_goToSignIn')}
        </Link>
      </Card>
    </AuthSplitLayout>
  );
}
