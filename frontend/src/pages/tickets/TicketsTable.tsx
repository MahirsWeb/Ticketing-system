import { Link, useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { format } from 'date-fns';
import type { TicketListItem } from '../../types';
import { PriorityBadge } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

/// A sortable column heading. A real button, so it can be reached and used with the keyboard, and `aria-sort` tells a screen
/// reader which column the list is sorted by.
function SortableHeader({
  colKey,
  label,
  sortBy,
  sortDir,
  onSort,
}: {
  colKey: string;
  label: string;
  sortBy: string | null;
  sortDir: 'asc' | 'desc';
  onSort: (key: string) => void;
}) {
  const active = sortBy === colKey;
  return (
    <th scope="col" className="whitespace-nowrap px-3 py-1.5" aria-sort={active ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" onClick={() => onSort(colKey)} className="select-none font-semibold uppercase hover:text-slate-700">
        {label}
        {active && (
          <span aria-hidden className="ml-1">
            {sortDir === 'asc' ? '▲' : '▼'}
          </span>
        )}
      </button>
    </th>
  );
}

/** The tickets of one page. The client and the priority are for staff only. `emptyText` is what is said when there are none to show. */
export function TicketsTable({
  items,
  isStaff,
  sortBy,
  sortDir,
  onSort,
  emptyText,
}: {
  items: TicketListItem[];
  isStaff: boolean;
  sortBy: string | null;
  sortDir: 'asc' | 'desc';
  onSort: (key: string) => void;
  emptyText: string;
}) {
  const { t: tr } = useLanguage();
  const navigate = useNavigate();
  const sorting = { sortBy, sortDir, onSort };
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <caption className="sr-only">{tr('nav_tickets')}</caption>
        <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-600">
          <tr>
            <SortableHeader colKey="ticketNumber" label="#" {...sorting} />
            <SortableHeader colKey="title" label={tr('table_title')} {...sorting} />
            <SortableHeader colKey="createdAt" label={tr('table_created')} {...sorting} />
            {isStaff && (
              <th scope="col" className="whitespace-nowrap px-3 py-1.5">
                {tr('table_client')}
              </th>
            )}
            {isStaff && <SortableHeader colKey="priority" label={tr('table_priority')} {...sorting} />}
            <th scope="col" className="whitespace-nowrap px-3 py-1.5">
              {tr('table_assignedTo')}
            </th>
          </tr>
        </thead>
        <tbody>
          {items.map((t, i) => (
            <tr
              key={t.id}
              onClick={() => navigate(`/tickets/${t.id}`)}
              className={clsx('cursor-pointer border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}
            >
              <td className="whitespace-nowrap px-3 py-1.5 font-medium">
                {/* The whole row can be clicked with a mouse; this link is what the keyboard and a screen reader use. */}
                <Link to={`/tickets/${t.id}`} onClick={(e) => e.stopPropagation()} className="text-blue-700 hover:underline">
                  #{t.ticketNumber}
                </Link>
              </td>
              <td className="max-w-[220px] truncate px-3 py-1.5">{t.title}</td>
              <td className="whitespace-nowrap px-3 py-1.5 text-slate-600">{format(new Date(t.createdAt), 'dd.MM.yyyy HH:mm')}</td>
              {isStaff && (
                <td className="whitespace-nowrap px-3 py-1.5">
                  <Link to={`/users/${t.clientId}`} onClick={(e) => e.stopPropagation()} className="text-blue-700 hover:underline">
                    {t.clientName}
                  </Link>
                </td>
              )}
              {isStaff && (
                <td className="whitespace-nowrap px-3 py-1.5">
                  <PriorityBadge priority={t.priority} />
                </td>
              )}
              <td className="whitespace-nowrap px-3 py-1.5">{t.assignedToName ?? '—'}</td>
            </tr>
          ))}
          {items.length === 0 && (
            <tr>
              <td colSpan={isStaff ? 6 : 4} className="px-3 py-8 text-center text-slate-600">
                {emptyText}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
