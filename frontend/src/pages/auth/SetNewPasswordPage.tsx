import { useState } from 'react';
import { useLocation, useNavigate, Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Button, Card, ErrorText, Input, Label, SuccessText, Field } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';
import { useLanguage } from '../../i18n/LanguageContext';

export default function SetNewPasswordPage() {
  const { t } = useLanguage();
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
      setError(t('auth_pwMismatch'));
      return;
    }
    setLoading(true);
    try {
      const res = await authApi.setNewPassword(email, temporaryPassword, newPassword, confirmNewPassword);
      setSuccess(res.message ?? t('auth_setPw_ok'));
      setTimeout(() => navigate('/login'), 2500);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? t('auth_setPw_fail'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthSplitLayout>
      <Card className="w-full max-w-sm p-8">
        <h1 className="mb-1 text-xl font-bold text-slate-900">{t('auth_setPw_title')}</h1>
        <p className="mb-6 text-sm text-slate-600">
          {t('auth_setPw_subtitle')}
        </p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <Field>
            <Label>{t('common_email')}</Label>
            <Input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </Field>
          <Field>
            <Label>{t('auth_tempPassword')}</Label>
            <Input type="password" required value={temporaryPassword} onChange={(e) => setTemporaryPassword(e.target.value)} />
          </Field>
          <Field>
            <Label>{t('auth_newPassword')}</Label>
            <Input type="password" required minLength={10} value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
          </Field>
          <Field>
            <Label>{t('auth_confirmNewPassword')}</Label>
            <Input type="password" required minLength={10} value={confirmNewPassword} onChange={(e) => setConfirmNewPassword(e.target.value)} />
          </Field>
          <ErrorText>{error}</ErrorText>
          <SuccessText>{success}</SuccessText>
          <Button type="submit" variant="pill" className="w-full" disabled={loading}>
            {loading ? t('common_saving') : t('auth_setPw_submit')}
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
