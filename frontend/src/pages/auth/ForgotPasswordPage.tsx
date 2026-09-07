import { useState } from 'react';
import { Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Button, Card, Input, Label, SuccessText } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    try {
      await authApi.forgotPassword(email);
    } finally {
      setLoading(false);
      setSent(true);
    }
  }

  return (
    <AuthSplitLayout>
      <Card className="w-full max-w-sm p-8">
        <h1 className="mb-1 text-xl font-bold text-slate-900">Forgot password</h1>
        <p className="mb-6 text-sm text-slate-500">Enter your email and we'll send you a reset link.</p>
        {!sent ? (
          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <Label>Email</Label>
              <Input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} autoFocus />
            </div>
            <Button type="submit" variant="pill" className="w-full" disabled={loading}>
              {loading ? 'Sending…' : 'Send reset link'}
            </Button>
          </form>
        ) : (
          <SuccessText>If that email exists in our system, a reset link has been sent.</SuccessText>
        )}
        <div className="mt-4 text-center text-sm">
          <Link to="/login" className="text-blue-700 hover:underline">
            Back to sign in
          </Link>
        </div>
      </Card>
    </AuthSplitLayout>
  );
}
