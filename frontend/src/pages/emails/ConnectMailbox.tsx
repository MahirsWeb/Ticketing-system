import { useState } from 'react';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { Button, Card } from '../../components/ui';
import { createPkce, randomToken } from '../../lib/oauth';
import { redirectTo } from '../../lib/redirect';
import { buildMicrosoftAuthUrl } from './microsoftAuth';

/** The offer to connect the mailbox of the branch, and the first step of doing it: going to Microsoft to sign in with the mailbox's own account. */
export function ConnectMailbox({ branchName }: { branchName: string | null }) {
  const [connecting, setConnecting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleConnect() {
    setConnecting(true);
    setError(null);
    let leaving = false;
    try {
      const { clientId } = await emailIntegrationApi.getConfig();
      if (!clientId) {
        setError('Outlook integration is not configured yet (missing Client ID). Ask an admin to finish setup.');
        return;
      }
      // PKCE makes a stolen authorization code useless without the secret that stays in this tab. The browser can make it only on a
      // secure connection (https, or localhost); on a plain-http page the flow used to carry on without it, and silently.
      const pkce = await createPkce();
      if (!pkce) {
        setError('A mailbox can only be connected over a secure connection (https). Open the application at its https address and try again.');
        return;
      }
      const redirectUri = `${window.location.origin}/emails/callback`;
      const state = randomToken(16);
      sessionStorage.setItem('ms_oauth_state', state);
      sessionStorage.setItem('ms_oauth_verifier', pkce.verifier);
      leaving = true; // the page is about to be replaced by Microsoft's; the button stays as it is until then
      redirectTo(buildMicrosoftAuthUrl(clientId, redirectUri, state, pkce.challenge));
    } catch {
      setError('The connection to Microsoft could not be started. Check the connection and try again.');
    } finally {
      if (!leaving) setConnecting(false);
    }
  }

  return (
    <div className="mx-auto max-w-lg">
      <h1 className="mb-2 text-xl font-bold text-slate-900">Emails</h1>
      <Card className="p-6 text-center">
        <p className="mb-4 text-sm text-slate-600">
          Connect your branch's shared mailbox ({branchName}) to see incoming emails here and turn them into tickets. Everyone in your branch will see the same
          inbox and marks — sign in with the branch's own mailbox account, not your personal one.
        </p>
        {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
        <Button onClick={handleConnect} disabled={connecting}>
          {connecting ? 'Redirecting…' : '🔗 Connect branch mailbox'}
        </Button>
      </Card>
    </div>
  );
}
