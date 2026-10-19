function toBase64Url(bytes: Uint8Array): string {
  let binary = '';
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** An unpredictable token from the browser's cryptographic generator (Math.random() is not one: its output can be predicted). */
export function randomToken(byteLength = 32): string {
  const bytes = new Uint8Array(byteLength);
  crypto.getRandomValues(bytes);
  return toBase64Url(bytes);
}

/**
 * PKCE (RFC 7636): the secret `verifier` stays in this browser tab; only its SHA-256 `challenge` goes to Microsoft. The
 * authorization code can then be exchanged only by whoever holds the verifier.
 * crypto.subtle exists only in secure contexts (https or localhost). On a plain-http intranet it is missing, and the flow
 * carries on with the `state` check alone instead of failing, so null is returned.
 */
export async function createPkce(): Promise<{ verifier: string; challenge: string } | null> {
  if (typeof crypto === 'undefined' || !crypto.subtle) return null;
  const verifier = randomToken(48);
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  return { verifier, challenge: toBase64Url(new Uint8Array(digest)) };
}
