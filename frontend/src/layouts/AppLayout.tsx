import { Suspense, useEffect, useId, useRef, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { ErrorBoundary } from '../components/ErrorBoundary';
import { useAuthStore } from '../store/authStore';
import { NotificationBell } from '../components/NotificationBell';
import { PhoneNumberBanner } from '../components/PhoneNumberBanner';
import { LanguageSwitcher } from '../components/LanguageSwitcher';
import { useLanguage } from '../i18n/LanguageContext';
import type { TranslationKey } from '../i18n/translations';
import { useTicketCounts } from '../api/ticketQueries';
import { authApi } from '../api/auth';
import { usersApi } from '../api/users';
import clsx from 'clsx';

function useNavByRole(): Record<string, { to: string; label: string; ticketsDropdown?: boolean }[]> {
  const { t } = useLanguage();
  return {
    Admin: [
      { to: '/', label: t('nav_dashboard') },
      { to: '/tickets', label: t('nav_tickets'), ticketsDropdown: true },
      { to: '/tasks', label: t('nav_task') },
      { to: '/ai-assistant', label: t('nav_aiAssistant') },
      { to: '/users', label: t('nav_users') },
      { to: '/settings', label: t('nav_settings') },
    ],
    Employee: [
      { to: '/', label: t('nav_dashboard') },
      { to: '/tickets', label: t('nav_tickets'), ticketsDropdown: true },
      { to: '/tasks', label: t('nav_task') },
      { to: '/ai-assistant', label: t('nav_aiAssistant') },
      { to: '/emails', label: t('nav_emails') },
      { to: '/users', label: t('nav_clients') },
    ],
    Client: [
      { to: '/tickets', label: t('nav_myTickets'), ticketsDropdown: true },
      { to: '/tickets/new', label: t('nav_submitTicket') },
    ],
  };
}

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  clsx(
    'shrink-0 whitespace-nowrap border-b-2 px-4 py-2.5 text-sm font-semibold transition',
    isActive ? 'border-blue-400 bg-slate-200 text-[#1a2b4c]' : 'border-transparent text-slate-200 hover:bg-white/10 hover:text-white'
  );

function TicketsNavItem({ to, label }: { to: string; label: string }) {
  const user = useAuthStore((s) => s.user);
  const { t } = useLanguage();
  const { data: counts, refetch } = useTicketCounts();
  // Looking at the menu asks again, so the numbers are right when they are read even if a change was made somewhere the page did not hear about.
  const load = () => void refetch();
  const [open, setOpen] = useState(false);
  const panelId = useId();

  // Escape closes the shortcuts from wherever the focus is, and so does moving the focus or clicking somewhere else.
  const rootRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const escape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    const away = (e: Event) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('keydown', escape);
    document.addEventListener('focusin', away);
    document.addEventListener('mousedown', away);
    return () => {
      document.removeEventListener('keydown', escape);
      document.removeEventListener('focusin', away);
      document.removeEventListener('mousedown', away);
    };
  }, [open]);

  const rows: { label: string; value: number | undefined; status?: string; mine?: boolean }[] = [
    { label, value: counts?.total },
    ...(user?.role === 'Employee' ? [{ label: t('nav_myTicketsCount'), value: counts?.mine, status: 'Open,InProgress,Resolved', mine: true }] : []),
    { label: t('nav_opened'), value: counts?.opened, status: 'Open,InProgress,Resolved' },
    { label: t('nav_closed'), value: counts?.closed, status: 'Closed' },
    { label: t('nav_new'), value: counts?.new, status: 'New' },
  ];

  function hrefFor(r: (typeof rows)[number]) {
    const params = new URLSearchParams();
    if (r.status) params.set('status', r.status);
    if (r.mine) params.set('mine', '1');
    const qs = params.toString();
    return qs ? `/tickets?${qs}` : '/tickets';
  }

  // A disclosure: the label is an ordinary link to the list, and a separate button opens the shortcuts under it. The panel is
  // not in the tab order while it is closed (it is invisible, not just transparent), its items are real links, and Escape closes
  // it. The mouse opens it by hovering (CSS), as before; the state is for the keyboard and for screen readers.
  return (
    <div
      role="group"
      aria-label={label}
      ref={rootRef}
      className="group relative flex items-stretch"
    >
      <NavLink to={to} end className={(p) => clsx(navLinkClass(p), 'pr-1')} onMouseEnter={load}>
        {label}
      </NavLink>
      <button
        type="button"
        aria-label={`${label}: ${t('nav_ticketMenu')}`}
        aria-haspopup="true"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => {
          load();
          setOpen((o) => !o);
        }}
        className="shrink-0 border-b-2 border-transparent px-2 text-[10px] text-slate-300 transition hover:bg-white/10 hover:text-white"
      >
        <span aria-hidden>▾</span>
      </button>
      <div
        id={panelId}
        className={clsx(
          'absolute left-0 top-full z-30 w-48 transition-all duration-200 ease-out',
          open
            ? 'visible translate-y-0 opacity-100'
            : 'invisible -translate-y-1 opacity-0 group-hover:visible group-hover:translate-y-0 group-hover:opacity-100'
        )}
      >
        <div className="mt-1 overflow-hidden rounded-md border border-slate-200 bg-white py-1 text-left shadow-lg">
          {rows.map((r) => (
            <Link
              key={r.label}
              to={hrefFor(r)}
              onClick={() => setOpen(false)}
              className="flex items-center justify-between px-4 py-2 text-sm font-normal text-slate-700 hover:bg-slate-50 focus-visible:bg-slate-50"
            >
              <span>{r.label}</span>
              <span className="font-semibold text-slate-600">{r.value ?? '—'}</span>
            </Link>
          ))}
        </div>
      </div>
    </div>
  );
}

export default function AppLayout() {
  const user = useAuthStore((s) => s.user);
  const signOut = useAuthStore((s) => s.signOut);
  const navigate = useNavigate();
  const location = useLocation();
  const { t, language } = useLanguage();
  const navByRole = useNavByRole();
  const [signingOut, setSigningOut] = useState(false);
  const [signOutFailed, setSignOutFailed] = useState(false);

  // E-mails are composed on the server, which does not know the language the browser shows: it is told, when the person signs in and
  // whenever they change it. A failure is not worth showing (the next load tells it again), so it is dropped.
  const userId = user?.id;
  useEffect(() => {
    if (!userId) return;
    usersApi.setMyLanguage(language).catch(() => undefined);
  }, [userId, language]);

  if (!user) return null;
  const nav = navByRole[user.role] ?? [];

  return (
    <div className="min-h-screen bg-slate-200">
      <header className="border-b border-slate-300 bg-[#1a2b4c]">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-3">
          <div className="text-lg font-bold text-white">{t('appName')}</div>
          <div className="flex items-center gap-4">
            <LanguageSwitcher dark />
            <div className="hidden text-right text-xs text-slate-300 sm:block">
              <div className="font-semibold text-white">
                {user.firstName} {user.lastName}
              </div>
              <div>{t(`role_${user.role}` as TranslationKey)}</div>
            </div>
            <NotificationBell />
            <button
              disabled={signingOut}
              onClick={async () => {
                setSigningOut(true);
                setSignOutFailed(false);
                try {
                  // Wait for the server: until it confirms, the refresh cookie (14 days) is still a live session. Leaving the page
                  // as "signed out" while it is not would let the next person at a shared computer carry on as this one.
                  await authApi.logout();
                } catch {
                  setSignOutFailed(true);
                  setSigningOut(false);
                  return;
                }
                signOut();
                navigate('/login', { replace: true });
              }}
              className="rounded-md border border-white/20 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-white/10 disabled:opacity-60"
            >
              {t('signOut')}
            </button>
          </div>
        </div>

        <nav aria-label={t('common_mainNav')} className="mx-auto flex max-w-5xl gap-1 px-4 max-sm:overflow-x-auto">
          {nav.map((item) =>
            item.ticketsDropdown ? (
              <TicketsNavItem key={item.to} to={item.to} label={item.label} />
            ) : (
              <NavLink key={item.to} to={item.to} end={item.to === '/'} className={navLinkClass}>
                {item.label}
              </NavLink>
            )
          )}
        </nav>
      </header>

      {signOutFailed && (
        <div role="alert" className="bg-red-50 px-4 py-2 text-center text-sm text-red-800">
          {t('common_signOutFailed')}
        </div>
      )}

      {user.role === 'Client' && <PhoneNumberBanner />}

      <main className="mx-auto max-w-5xl px-4 py-6">
        {/* Only the page area waits for a screen's code; the header and navigation stay put. */}
        <ErrorBoundary inline resetKey={location.pathname}>
          <Suspense fallback={<div role="status" className="py-16 text-center text-sm text-slate-600">{t('common_loading')}</div>}>
            <Outlet />
          </Suspense>
        </ErrorBoundary>
      </main>
    </div>
  );
}
