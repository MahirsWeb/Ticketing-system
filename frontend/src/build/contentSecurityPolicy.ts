/**
 * The origin of the API, or null when the application is served together with it (no address) or the address is not one that can be
 * written into a policy. Nothing but an http(s) origin is ever taken: a path, a query or anything that is not an address is dropped.
 */
export function apiOrigin(apiBaseUrl: string | undefined): string | null {
  if (!apiBaseUrl) return null;
  try {
    const url = new URL(apiBaseUrl);
    // A host name or an address and a port, and nothing else: an address can be parsed and still carry characters (a quote) that mean something in a policy.
    return (url.protocol === 'http:' || url.protocol === 'https:') && SAFE_ORIGIN.test(url.origin) ? url.origin : null;
  } catch {
    return null;
  }
}

const SAFE_ORIGIN = /^https?:\/\/(?:[a-z0-9-]+(?:\.[a-z0-9-]+)*|\[[0-9a-f:]+\])(?::\d{1,5})?$/i;

/**
 * The Content-Security-Policy that the built application carries in its own page (SEC-03), so that it holds whoever serves the files and
 * whether or not they set the header. Scripts come only from the application itself, styles from it and inline (the editors and the
 * components set `style` attributes), nothing is embedded, and the only other place the page may talk to or load pictures from is the API.
 *
 * A policy in a page cannot say who may frame it (`frame-ancestors`) or report what it blocks; those need the header of the server that
 * serves the files (see `deploy/`), which remains the better place for the whole policy.
 */
export function contentSecurityPolicy(apiBaseUrl: string | undefined): string {
  const api = apiOrigin(apiBaseUrl);
  // The live connection to the API (SignalR) is a WebSocket on the same address.
  const live = api ? [api, api.replace(/^http/, 'ws')] : [];
  return [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    `img-src ${["'self'", 'data:', 'blob:', ...(api ? [api] : [])].join(' ')}`,
    "font-src 'self' data:",
    `connect-src ${["'self'", ...live].join(' ')}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
  ].join('; ');
}
