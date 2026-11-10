import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { Card, Spinner } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

export default function EmailCallbackPage() {
  const { t } = useLanguage();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState('');
  const started = useRef(false);

  useEffect(() => {
    // The code can be exchanged only once, and the one-time values are spent as soon as they are read. React runs an effect twice when the application is
    // started in strict mode (as it is while it is being developed), and the second run would find them spent and report a failed security check.
    if (started.current) return;
    started.current = true;
    const code = params.get('code');
    const state = params.get('state');
    const expectedState = sessionStorage.getItem('ms_oauth_state');
    const codeVerifier = sessionStorage.getItem('ms_oauth_verifier') ?? undefined;
    const oauthError = params.get('error_description') || params.get('error');

    // The one-time values are spent whatever happens next: a page reload must not be able to replay them.
    sessionStorage.removeItem('ms_oauth_state');
    sessionStorage.removeItem('ms_oauth_verifier');

    if (oauthError) {
      setStatus('error');
      setMessage(oauthError);
      return;
    }
    if (!code) {
      setStatus('error');
      setMessage(t('email_noCode'));
      return;
    }
    // Fails closed: a missing stored state (this page opened directly, or from a link someone else crafted) is as much a
    // failure as a different one. It used to be skipped when nothing was stored, which let a forged callback through.
    if (!expectedState || state !== expectedState) {
      setStatus('error');
      setMessage(t('email_stateMismatch'));
      return;
    }

    const redirectUri = `${window.location.origin}/emails/callback`;
    emailIntegrationApi
      .connect(code, redirectUri, codeVerifier)
      .then((res) => {
        setStatus('success');
        setMessage(t('email_connected', { email: res.connectedEmail ?? '' }));
      })
      .catch((err) => {
        setStatus('error');
        setMessage(err?.response?.data?.message ?? t('email_connectFail'));
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the authorisation code can be exchanged only once, so this must run on arrival and never again
  }, []);

  // Goes on to the emails after a moment, so that the person can read that it worked, unless they have gone elsewhere in the meantime.
  useEffect(() => {
    if (status !== 'success') return;
    const timer = setTimeout(() => navigate('/emails'), 1500);
    return () => clearTimeout(timer);
  }, [status, navigate]);

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-100 px-4">
      <Card className="w-full max-w-sm p-8 text-center">
        <h1 className="mb-4 text-xl font-bold text-slate-900">{t('email_connecting')}</h1>
        {status === 'loading' && (
          <div role="status" aria-label={t('email_connectingAria')}>
            <Spinner className="mx-auto h-8 w-8 text-blue-700" />
          </div>
        )}
        {status !== 'loading' && (
          <p role={status === 'success' ? 'status' : 'alert'} className={status === 'success' ? 'text-green-700' : 'text-red-700'}>
            {message}
          </p>
        )}
        {status === 'error' && (
          <Link to="/emails" className="mt-6 inline-block text-sm text-blue-700 hover:underline">
            {t('email_backToEmails')}
          </Link>
        )}
      </Card>
    </div>
  );
}
