import type { TranslationKey } from '../i18n/translations';

/// Which name a page has in the browser tab (WCAG 2.4.2): a screen reader announces it when the page changes, and a person with many
/// tabs finds the right one by it. The first matching rule wins, so the specific routes come before the general ones.
const RULES: { test: (path: string) => boolean; key: TranslationKey }[] = [
  { test: (p) => p === '/login', key: 'login_signIn' },
  { test: (p) => ['/set-new-password', '/forgot-password', '/reset-password', '/verify-email'].includes(p), key: 'title_account' },
  { test: (p) => p === '/tickets/new', key: 'nav_submitTicket' },
  { test: (p) => p === '/tickets/new-on-behalf', key: 'title_newOnBehalf' },
  { test: (p) => /^\/tickets\/[^/]+$/.test(p), key: 'title_ticket' },
  { test: (p) => p === '/tickets', key: 'nav_tickets' },
  { test: (p) => p === '/tasks', key: 'nav_task' },
  { test: (p) => /^\/users\/[^/]+$/.test(p), key: 'title_user' },
  { test: (p) => p === '/users', key: 'nav_users' },
  { test: (p) => p.startsWith('/companies/'), key: 'title_company' },
  { test: (p) => p === '/ai-assistant', key: 'nav_aiAssistant' },
  { test: (p) => p === '/knowledge-base/similar', key: 'title_similar' },
  { test: (p) => p === '/settings', key: 'nav_settings' },
  { test: (p) => p === '/emails' || p === '/emails/callback', key: 'nav_emails' },
  { test: (p) => p === '/', key: 'nav_dashboard' }
];

export function titleKeyFor(pathname: string): TranslationKey {
  const path = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname;
  return RULES.find((r) => r.test(path))?.key ?? 'title_notFound';
}
