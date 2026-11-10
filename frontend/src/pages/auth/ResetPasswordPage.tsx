import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Button, Card, ErrorText, Input, Label, Field } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';
import { useLanguage } from '../../i18n/LanguageContext';

export default function ResetPasswordPage() {
  const { t } = useLanguage();
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
      setError(t('auth_pwMismatch'));
      return;
    }
    setLoading(true);
    try {
      await authApi.resetPassword(userId, token, newPassword, confirmNewPassword);
      navigate('/login');
    } catch (err: any) {
      setError(err?.response?.data?.message ?? t('auth_resetLinkInvalid'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthSplitLayout>
      <Card className="w-full max-w-sm p-8">
        <h1 className="mb-6 text-xl font-bold text-slate-900">{t('auth_reset_title')}</h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <Field>
            <Label>{t('auth_newPassword')}</Label>
            <Input type="password" required minLength={10} value={newPassword} onChange={(e) => setNewPassword(e.target.value)} autoFocus />
          </Field>
          <Field>
            <Label>{t('auth_confirmNewPassword')}</Label>
            <Input type="password" required minLength={10} value={confirmNewPassword} onChange={(e) => setConfirmNewPassword(e.target.value)} />
          </Field>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" variant="pill" className="w-full" disabled={loading}>
            {loading ? t('common_saving') : t('auth_reset_submit')}
          </Button>
        </form>
        <div className="mt-4 text-center text-sm">
          <Link to="/login" className="text-blue-700 hover:underline">
            {t('auth_backToSignIn')}
          </Link>
        </div>
      </Card>
    </AuthSplitLayout>
  );
}
