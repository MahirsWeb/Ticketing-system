import { useState } from 'react';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { Button, Card } from '../../components/ui';
import { createPkce, randomToken } from '../../lib/oauth';
import { redirectTo } from '../../lib/redirect';
import { buildMicrosoftAuthUrl } from './microsoftAuth';
import { useLanguage } from '../../i18n/LanguageContext';

/** The offer to connect the mailbox of the branch, and the first step of doing it: going to Microsoft to sign in with the mailbox's own account. */
export function ConnectMailbox({ branchName }: { branchName: string | null }) {
  const { t } = useLanguage();
  const [connecting, setConnecting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleConnect() {
    setConnecting(true);
    setError(null);
    let leaving = false;
    try {
      const { clientId } = await emailIntegrationApi.getConfig();
      if (!clientId) {
        setError(t('email_notConfigured'));
        return;
      }
      // PKCE makes a stolen authorization code useless without the secret that stays in this tab. The browser can make it only on a
      // secure connection (https, or localhost); on a plain-http page the flow used to carry on without it, and silently.
      const pkce = await createPkce();
      if (!pkce) {
        setError(t('email_httpsOnly'));
        return;
      }
      const redirectUri = `${window.location.origin}/emails/callback`;
      const state = randomToken(16);
      sessionStorage.setItem('ms_oauth_state', state);
      sessionStorage.setItem('ms_oauth_verifier', pkce.verifier);
      leaving = true; // the page is about to be replaced by Microsoft's; the button stays as it is until then
      redirectTo(buildMicrosoftAuthUrl(clientId, redirectUri, state, pkce.challenge));
    } catch {
      setError(t('email_connectStartFail'));
    } finally {
      if (!leaving) setConnecting(false);
    }
  }

  return (
    <div className="mx-auto max-w-lg">
      <h1 className="mb-2 text-xl font-bold text-slate-900">{t('nav_emails')}</h1>
      <Card className="p-6 text-center">
        <p className="mb-4 text-sm text-slate-600">
          {t('email_connectDesc', { branch: branchName ?? '' })}
        </p>
        {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
        <Button onClick={handleConnect} disabled={connecting}>
          {connecting ? t('email_redirecting') : t('email_connectBtn')}
        </Button>
      </Card>
    </div>
  );
}
