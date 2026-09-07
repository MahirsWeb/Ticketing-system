import { useEffect, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../store/authStore';
import { NotificationBell } from '../components/NotificationBell';
import { PhoneNumberBanner } from '../components/PhoneNumberBanner';
import { LanguageSwitcher } from '../components/LanguageSwitcher';
import { useLanguage } from '../i18n/LanguageContext';
import { ticketsApi } from '../api/tickets';
import type { TicketCountsDto } from '../types';
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
    'border-b-2 px-4 py-2.5 text-sm font-semibold transition',
    isActive ? 'border-blue-400 bg-slate-200 text-[#1a2b4c]' : 'border-transparent text-slate-200 hover:bg-white/10 hover:text-white'
  );

function TicketsNavItem({ to, label }: { to: string; label: string }) {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const { t } = useLanguage();
  const [counts, setCounts] = useState<TicketCountsDto | null>(null);

  function load() {
    ticketsApi.counts().then(setCounts).catch(() => {});
  }

  useEffect(() => {
    load();
  }, []);

  const rows: { label: string; value: number | undefined; status?: string; mine?: boolean }[] = [
    { label, value: counts?.total },
    ...(user?.role === 'Employee' ? [{ label: t('nav_myTicketsCount'), value: counts?.mine, status: 'Open,InProgress,Resolved', mine: true }] : []),
    { label: t('nav_opened'), value: counts?.opened, status: 'Open,InProgress,Resolved' },
    { label: t('nav_closed'), value: counts?.closed, status: 'Closed' },
    { label: t('nav_new'), value: counts?.new, status: 'New' },
  ];

  // Rendered as a plain sibling of the other nav items — same tag, same classes — so it lines up
  // pixel-for-pixel with them. The dropdown panel nests inside this same <a>, so its rows are plain
  // clickable divs (not <Link>/<a>) to avoid invalid nested anchors.
  return (
    <NavLink
      to={to}
      end
      className={(p) => clsx(navLinkClass(p), 'group relative flex items-center gap-1')}
      onMouseEnter={load}
    >
      {label}
      <span className="text-[10px] opacity-60">▾</span>
      <div
        className={clsx(
          'absolute left-0 top-full z-30 w-48 -translate-y-1 opacity-0 pointer-events-none',
          'transition-all duration-200 ease-out group-hover:translate-y-0 group-hover:opacity-100 group-hover:pointer-events-auto'
        )}
      >
        <div className="mt-1 overflow-hidden rounded-md border border-slate-200 bg-white py-1 text-left shadow-lg">
          {rows.map((r) => (
            <div
              key={r.label}
              role="link"
              tabIndex={0}
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                const params = new URLSearchParams();
                if (r.status) params.set('status', r.status);
                if (r.mine) params.set('mine', '1');
                const qs = params.toString();
                navigate(qs ? `/tickets?${qs}` : '/tickets');
              }}
              className="flex cursor-pointer items-center justify-between px-4 py-2 text-sm font-normal text-slate-700 hover:bg-slate-50"
            >
              <span>{r.label}</span>
              <span className="font-semibold text-slate-500">{r.value ?? '—'}</span>
            </div>
          ))}
        </div>
      </div>
    </NavLink>
  );
}

export default function AppLayout() {
  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);
  const navigate = useNavigate();
  const { t } = useLanguage();
  const navByRole = useNavByRole();

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
              <div>{user.role}</div>
            </div>
            <NotificationBell />
            <button
              onClick={() => {
                logout();
                navigate('/login');
              }}
              className="rounded-md border border-white/20 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-white/10"
            >
              {t('signOut')}
            </button>
          </div>
        </div>

        <nav className="mx-auto flex max-w-5xl gap-1 px-4">
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

      {user.role === 'Client' && <PhoneNumberBanner />}

      <main className="mx-auto max-w-5xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}
