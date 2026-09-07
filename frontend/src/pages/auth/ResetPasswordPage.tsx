import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Button, Card, ErrorText, Input, Label } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';

export default function ResetPasswordPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const userId = params.get('userId') ?? '';
  const token = params.get('token') ?? '';

  const [newPassword, setNewPassword] = useState('');
  const [confirmNewPassword, setConfirmNewPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (newPassword !== confirmNewPassword) {
      setError('New password and confirmation do not match.');
      return;
    }
    setLoading(true);
    try {
      await authApi.resetPassword(userId, token, newPassword, confirmNewPassword);
      navigate('/login');
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'This reset link is invalid or has expired.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthSplitLayout>
      <Card className="w-full max-w-sm p-8">
        <h1 className="mb-6 text-xl font-bold text-slate-900">Reset your password</h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <Label>New password</Label>
            <Input type="password" required minLength={10} value={newPassword} onChange={(e) => setNewPassword(e.target.value)} autoFocus />
          </div>
          <div>
            <Label>Confirm new password</Label>
            <Input type="password" required minLength={10} value={confirmNewPassword} onChange={(e) => setConfirmNewPassword(e.target.value)} />
          </div>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" variant="pill" className="w-full" disabled={loading}>
            {loading ? 'Saving…' : 'Reset password'}
          </Button>
        </form>
        <div className="mt-4 text-center text-sm">
          <Link to="/login" className="text-blue-700 hover:underline">
            Back to sign in
          </Link>
        </div>
      </Card>
    </AuthSplitLayout>
  );
}
