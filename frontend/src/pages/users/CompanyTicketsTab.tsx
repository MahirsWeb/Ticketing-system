import { useState } from 'react';
import { Link } from 'react-router-dom';
import { format } from 'date-fns';
import clsx from 'clsx';
import type { UseQueryResult } from '@tanstack/react-query';
import { useTicketList } from '../../api/ticketQueries';
import { Card, ErrorText, PriorityBadge, Select, Spinner, StatusBadge } from '../../components/ui';
import type { UserListItemDto } from '../../types';

const PAGE_SIZE = 100;

/**
 * The tickets of the company, the first hundred, which can be narrowed to those of one client. It is asked for when the tab is open and
 * forgotten when it is closed; each client is a list of its own, so the answer for one is never shown as the answer for another.
 */
export function CompanyTicketsTab({ companyId, clients }: { companyId: string; clients: UseQueryResult<UserListItemDto[]> }) {
  const [clientId, setClientId] = useState('');
  const list = useTicketList({ companyId, clientId: clientId || undefined, pageSize: PAGE_SIZE });
  const tickets = list.data?.items ?? [];
  const total = list.data?.totalCount ?? 0;

  return (
    <Card className="overflow-hidden">
      <div className="flex items-center gap-2 border-b border-slate-100 p-3">
        <div className="w-64">
          <Select aria-label="Client" value={clientId} onChange={(e) => setClientId(e.target.value)}>
            <option value="">All clients</option>
            {(clients.data ?? []).map((c) => (
              <option key={c.id} value={c.id}>
                {c.firstName} {c.lastName} ({c.email})
              </option>
            ))}
          </Select>
        </div>
        {list.isSuccess && total > tickets.length && <p className="text-xs text-slate-600">Showing the first {tickets.length} of {total} tickets.</p>}
      </div>
      {list.isPending && (
        <div role="status" aria-label="Loading the tickets" className="flex justify-center py-10">
          <Spinner className="h-6 w-6 text-blue-700" />
        </div>
      )}
      {list.isError && (
        <div className="px-3 pb-3">
          <ErrorText>The tickets could not be loaded. Try again in a moment.</ErrorText>
        </div>
      )}
      {list.isSuccess && (
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-600">
            <tr>
              <th scope="col" className="px-3 py-1.5">#</th>
              <th scope="col" className="px-3 py-1.5">Title</th>
              <th scope="col" className="px-3 py-1.5">Status</th>
              <th scope="col" className="px-3 py-1.5">Priority</th>
              <th scope="col" className="px-3 py-1.5">Client</th>
              <th scope="col" className="px-3 py-1.5">Assigned to</th>
              <th scope="col" className="px-3 py-1.5">Created</th>
            </tr>
          </thead>
          <tbody>
            {tickets.map((t, i) => (
              <tr key={t.id} className={clsx('border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}>
                <td className="whitespace-nowrap px-3 py-1.5">
                  <Link to={`/tickets/${t.id}`} className="font-medium text-blue-700 hover:underline">
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
                <td className="whitespace-nowrap px-3 py-1.5">{t.clientName}</td>
                <td className="whitespace-nowrap px-3 py-1.5">{t.assignedToName ?? '—'}</td>
                <td className="whitespace-nowrap px-3 py-1.5 text-slate-600">{format(new Date(t.createdAt), 'dd.MM.yyyy HH:mm')}</td>
              </tr>
            ))}
            {tickets.length === 0 && (
              <tr>
                <td colSpan={7} className="px-3 py-8 text-center text-slate-600">
                  No tickets found.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </Card>
  );
}
