import { useState } from 'react';
import { useLocation, useNavigate, Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Button, Card, ErrorText, Input, Label, SuccessText } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';

export default function SetNewPasswordPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const state = (location.state ?? {}) as { email?: string; temporaryPassword?: string };

  const [email, setEmail] = useState(state.email ?? '');
  const [temporaryPassword, setTemporaryPassword] = useState(state.temporaryPassword ?? '');
  const [newPassword, setNewPassword] = useState('');
  const [confirmNewPassword, setConfirmNewPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSuccess(null);
    if (newPassword !== confirmNewPassword) {
      setError('New password and confirmation do not match.');
      return;
    }
    setLoading(true);
    try {
      const res = await authApi.setNewPassword(email, temporaryPassword, newPassword, confirmNewPassword);
      setSuccess(res.message ?? 'Password set successfully.');
      setTimeout(() => navigate('/login'), 2500);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not set your new password.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthSplitLayout>
      <Card className="w-full max-w-sm p-8">
        <h1 className="mb-1 text-xl font-bold text-slate-900">Set your password</h1>
        <p className="mb-6 text-sm text-slate-500">
          This is your first sign-in. Please choose a permanent password to replace the temporary one.
        </p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <Label>Email</Label>
            <Input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
          <div>
            <Label>Temporary password</Label>
            <Input type="password" required value={temporaryPassword} onChange={(e) => setTemporaryPassword(e.target.value)} />
          </div>
          <div>
            <Label>New password</Label>
            <Input type="password" required minLength={10} value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
          </div>
          <div>
            <Label>Confirm new password</Label>
            <Input type="password" required minLength={10} value={confirmNewPassword} onChange={(e) => setConfirmNewPassword(e.target.value)} />
          </div>
          <ErrorText>{error}</ErrorText>
          <SuccessText>{success}</SuccessText>
          <Button type="submit" variant="pill" className="w-full" disabled={loading}>
            {loading ? 'Saving…' : 'Set password'}
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
