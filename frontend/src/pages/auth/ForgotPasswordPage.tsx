import { useState } from 'react';
import { Link } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { Button, Card, Input, Label, SuccessText, Field } from '../../components/ui';
import { AuthSplitLayout } from '../../layouts/AuthSplitLayout';
import { useLanguage } from '../../i18n/LanguageContext';

export default function ForgotPasswordPage() {
  const { t } = useLanguage();
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
        <h1 className="mb-1 text-xl font-bold text-slate-900">{t('auth_forgot_title')}</h1>
        <p className="mb-6 text-sm text-slate-600">{t('auth_forgot_subtitle')}</p>
        {!sent ? (
          <form onSubmit={handleSubmit} className="space-y-4">
            <Field>
              <Label>{t('common_email')}</Label>
              <Input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} autoFocus />
            </Field>
            <Button type="submit" variant="pill" className="w-full" disabled={loading}>
              {loading ? t('common_sending') : t('auth_forgot_submit')}
            </Button>
          </form>
        ) : (
          <SuccessText>{t('auth_forgot_sent')}</SuccessText>
        )}
        <div className="mt-4 text-center text-sm">
          <Link to="/login" className="text-blue-700 hover:underline">
            {t('auth_backToSignIn')}
          </Link>
        </div>
      </Card>
    </AuthSplitLayout>
  );
}
