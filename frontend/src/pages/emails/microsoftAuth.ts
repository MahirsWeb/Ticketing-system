/** The address of Microsoft's sign-in page for a mailbox: the application's client id, where to come back to, the one-time state and the PKCE challenge. */
export function buildMicrosoftAuthUrl(clientId: string, redirectUri: string, state: string, codeChallenge: string) {
  const params = new URLSearchParams({
    client_id: clientId,
    response_type: 'code',
    redirect_uri: redirectUri,
    response_mode: 'query',
    scope: 'offline_access Mail.Read User.Read',
    state,
    // Force Microsoft's account picker every time — without this it silently reuses whatever
    // Microsoft account already has an active browser session, which isn't necessarily the
    // branch mailbox account the person meant to sign in with.
    prompt: 'select_account',
    code_challenge: codeChallenge,
    code_challenge_method: 'S256',
  });
  return `https://login.microsoftonline.com/common/oauth2/v2.0/authorize?${params.toString()}`;
}
