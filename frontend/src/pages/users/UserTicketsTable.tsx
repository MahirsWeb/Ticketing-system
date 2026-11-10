import { Link, useNavigate } from 'react-router-dom';
import { format } from 'date-fns';
import clsx from 'clsx';
import { useUserTickets } from '../../api/userQueries';
import { Card, ErrorText, PriorityBadge, Spinner, StatusBadge } from '../../components/ui';
import type { UserListItemDto } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';

/** The tickets a client submitted, or those a member of the staff has. Only the first hundred are listed; the heading says how many there are. */
export function UserTicketsTable({ user }: { user: UserListItemDto }) {
  const { t } = useLanguage();
  const navigate = useNavigate();
  const list = useUserTickets(user);
  const tickets = list.data?.items ?? [];
  const total = list.data?.totalCount ?? tickets.length;
  const isClient = user.role === 'Client';

  return (
    <div>
      <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">
        Tickets {isClient ? 'submitted' : 'assigned'}
        {list.isSuccess ? ` (${total})` : ''}
      </h2>
      {list.isSuccess && total > tickets.length && <p className="mb-2 text-xs text-slate-600">Showing the first {tickets.length}.</p>}
      <Card className="overflow-hidden">
        {list.isPending ? (
          <div className="flex justify-center py-10">
            <Spinner className="h-6 w-6 text-blue-700" />
          </div>
        ) : list.isError ? (
          <div className="px-3 py-4">
            <ErrorText>{t('ticket_loadError')}</ErrorText>
          </div>
        ) : (
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-600">
              <tr>
                <th className="px-3 py-1.5">#</th>
                <th className="px-3 py-1.5">{t('table_title')}</th>
                <th className="px-3 py-1.5">{t('col_status')}</th>
                <th className="px-3 py-1.5">{t('table_priority')}</th>
                <th className="px-3 py-1.5">{isClient ? t('table_assignedTo') : 'Client'}</th>
                <th className="px-3 py-1.5">{t('table_created')}</th>
              </tr>
            </thead>
            <tbody>
              {tickets.map((t, i) => (
                <tr
                  key={t.id}
                  className={clsx('cursor-pointer border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}
                  onClick={() => navigate(`/tickets/${t.id}`)}
                >
                  <td className="whitespace-nowrap px-3 py-1.5">
                    <Link to={`/tickets/${t.id}`} className="font-medium text-blue-700 hover:underline" onClick={(e) => e.stopPropagation()}>
                      #{t.ticketNumber}
                    </Link>
                  </td>
                  <td className="max-w-[220px] truncate px-3 py-1.5">{t.title}</td>
                  <td className="whitespace-nowrap px-3 py-1.5">
                    <StatusBadge status={t.status} />
                  </td>
                  <td className="whitespace-nowrap px-3 py-1.5">
                    <PriorityBadge priority={t.priority} />
                  </td>
                  <td className="whitespace-nowrap px-3 py-1.5">{isClient ? t.assignedToName ?? '—' : t.clientName}</td>
                  <td className="whitespace-nowrap px-3 py-1.5 text-slate-600">{format(new Date(t.createdAt), 'dd.MM.yyyy HH:mm')}</td>
                </tr>
              ))}
              {tickets.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-3 py-8 text-center text-slate-600">
                    No tickets found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </Card>
    </div>
  );
}
