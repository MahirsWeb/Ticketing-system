import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { format } from 'date-fns';
import clsx from 'clsx';
import type { UseQueryResult } from '@tanstack/react-query';
import { Button, Card, ErrorText, Input, StatusPill } from '../../components/ui';
import type { UserListItemDto } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';

/** The clients of the company, with a search over their names and e-mail addresses (ignoring capital letters and the spaces around what was typed) and a way to add one. */
export function CompanyUsersTab({ companyId, clients }: { companyId: string; clients: UseQueryResult<UserListItemDto[]> }) {
  const { t } = useLanguage();
  const [search, setSearch] = useState('');
  const all = clients.data;
  const shown = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!all || !q) return all ?? [];
    return all.filter((c) => `${c.firstName} ${c.lastName} ${c.email}`.toLowerCase().includes(q));
  }, [all, search]);

  // What the empty table says depends on why it is empty: not loaded yet, could not be, nobody there, or nobody matches.
  const message = clients.isPending ? t('compusers_loading') : clients.isError ? null : all && all.length === 0 ? t('compusers_empty') : t('compusers_noMatch');

  return (
    <Card className="overflow-hidden">
      <div className="flex items-center justify-between gap-3 border-b border-slate-100 p-3">
        <Input placeholder={t('compusers_searchPlaceholder')} value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        <Link to={`/users?companyId=${companyId}`}>
          <Button variant="secondary" className="whitespace-nowrap text-xs">
            {t('compusers_addUser')}
          </Button>
        </Link>
      </div>
      {clients.isError && (
        <div className="px-3 pb-3">
          <ErrorText>{t('compusers_loadFail')}</ErrorText>
        </div>
      )}
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-600">
          <tr>
            <th scope="col" className="px-4 py-2">{t('common_name')}</th>
            <th scope="col" className="px-4 py-2">{t('common_email')}</th>
            <th scope="col" className="px-4 py-2">{t('profile_phone')}</th>
            <th scope="col" className="px-4 py-2">{t('table_created')}</th>
            <th scope="col" className="px-4 py-2">{t('col_status')}</th>
          </tr>
        </thead>
        <tbody>
          {shown.map((c, i) => (
            <tr key={c.id} className={clsx('border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}>
              <td className="px-4 py-2">
                {c.firstName} {c.lastName}
              </td>
              <td className="px-4 py-2">{c.email}</td>
              <td className="px-4 py-2">{c.phoneNumber ?? '—'}</td>
              <td className="px-4 py-2 whitespace-nowrap text-slate-600">{format(new Date(c.createdAtUtc), 'dd.MM.yyyy')}</td>
              <td className="px-4 py-2">
                <StatusPill active={c.isActive} />
              </td>
            </tr>
          ))}
          {shown.length === 0 && message && (
            <tr>
              <td colSpan={5} className="px-4 py-8 text-center text-slate-600">
                {message}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </Card>
  );
}
