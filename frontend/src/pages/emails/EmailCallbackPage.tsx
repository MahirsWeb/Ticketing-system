import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { Card, Spinner } from '../../components/ui';

export default function EmailCallbackPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState('');

  useEffect(() => {
    const code = params.get('code');
    const state = params.get('state');
    const expectedState = sessionStorage.getItem('ms_oauth_state');
    const oauthError = params.get('error_description') || params.get('error');

    if (oauthError) {
      setStatus('error');
      setMessage(oauthError);
      return;
    }
    if (!code) {
      setStatus('error');
      setMessage('No authorization code was returned by Microsoft.');
      return;
    }
    if (expectedState && state !== expectedState) {
      setStatus('error');
      setMessage('Security check failed (state mismatch). Please try connecting again.');
      return;
    }

    const redirectUri = `${window.location.origin}/emails/callback`;
    emailIntegrationApi
      .connect(code, redirectUri)
      .then((res) => {
        setStatus('success');
        setMessage(`Connected: ${res.connectedEmail}`);
        sessionStorage.removeItem('ms_oauth_state');
        setTimeout(() => navigate('/emails'), 1500);
      })
      .catch((err) => {
        setStatus('error');
        setMessage(err?.response?.data?.message ?? 'Could not connect your mailbox.');
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-100 px-4">
      <Card className="w-full max-w-sm p-8 text-center">
        <h1 className="mb-4 text-xl font-bold text-slate-900">Connecting Outlook…</h1>
        {status === 'loading' && <Spinner className="mx-auto h-8 w-8 text-blue-700" />}
        {status !== 'loading' && (
          <p className={status === 'success' ? 'text-green-700' : 'text-red-700'}>{message}</p>
        )}
        {status === 'error' && (
          <Link to="/emails" className="mt-6 inline-block text-sm text-blue-700 hover:underline">
            Back to Emails
          </Link>
        )}
      </Card>
    </div>
  );
}
