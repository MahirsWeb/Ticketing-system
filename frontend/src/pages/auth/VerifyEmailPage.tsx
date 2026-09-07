import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Card, Spinner } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';

export default function VerifyEmailPage() {
  const [params] = useSearchParams();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState('');

  useEffect(() => {
    const userId = params.get('userId');
    const token = params.get('token');
    if (!userId || !token) {
      setStatus('error');
      setMessage('This verification link is missing required information.');
      return;
    }
    authApi
      .verifyEmail(userId, token)
      .then((res) => {
        setStatus('success');
        setMessage(res.message ?? 'Email verified.');
      })
      .catch((err) => {
        setStatus('error');
        setMessage(err?.response?.data?.message ?? 'This verification link is invalid or has expired.');
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <AuthSplitLayout>
      <Card className="w-full max-w-sm p-8 text-center">
        <h1 className="mb-4 text-xl font-bold text-slate-900">Email verification</h1>
        {status === 'loading' && <Spinner className="mx-auto h-8 w-8 text-blue-700" />}
        {status !== 'loading' && (
          <p className={status === 'success' ? 'text-green-700' : 'text-red-700'}>{message}</p>
        )}
        <Link to="/login" className="mt-6 inline-block text-sm text-blue-700 hover:underline">
          Go to sign in
        </Link>
      </Card>
    </AuthSplitLayout>
  );
}
