import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import clsx from 'clsx';
import { useTicketList } from '../../api/ticketQueries';
import { useEmployees } from '../../api/userQueries';
import { useDepartments } from '../../api/lookupQueries';
import type { TicketListItem, UserListItemDto } from '../../types';
import { Button, Card, ErrorText, Input, PriorityBadge, Select, Spinner } from '../../components/ui';
import { DateRangePicker } from '../../components/DateRangePicker';
import { useAuthStore } from '../../store/authStore';
import { useDebouncedValue } from '../../lib/useDebouncedValue';
import { format } from 'date-fns';

const PAGE_SIZE = 20;

type TicketTab = 'all' | 'new' | 'opened' | 'answered' | 'closed';

const TABS: { key: TicketTab; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'new', label: 'New' },
  { key: 'opened', label: 'Opened' },
  { key: 'answered', label: 'Answered' },
  { key: 'closed', label: 'Closed' },
];

function tabToParams(tab: TicketTab): { status?: string; answered?: string } {
  switch (tab) {
    case 'new':
      return { status: 'New' };
    case 'opened':
      return { status: 'Open,InProgress,Resolved' };
    case 'answered':
      return { status: 'Open,InProgress,Resolved', answered: '1' };
    case 'closed':
      return { status: 'Closed' };
    default:
      return {};
  }
}

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

export default function TicketsListPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const isEmployee = user?.role === 'Employee';
  const isStaff = isAdmin || isEmployee;
  const { data: departments = [] } = useDepartments({ enabled: isAdmin });
  const { data: employees = [] } = useEmployees({ enabled: isStaff });

  // Everything the list is filtered, sorted and paged by lives in the address, so a refresh, the back button, a bookmark or a
  // link sent to a colleague shows the same list.
  const [searchParams, setSearchParams] = useSearchParams();
  const statusParam = searchParams.get('status') ?? '';
  const answeredParam = searchParams.get('answered') === '1';
  const mineOnly = searchParams.get('mine') === '1';
  const page = Math.max(1, Number(searchParams.get('page')) || 1);
  const departmentId = searchParams.get('dept') ?? '';
  const assignedToUserId = searchParams.get('assignee') ?? '';
  const search = searchParams.get('q') ?? '';
  const createdFromIso = searchParams.get('from');
  const createdToIso = searchParams.get('to');
  const sortBy = searchParams.get('sort');
  const sortDir: 'asc' | 'desc' = searchParams.get('dir') === 'desc' ? 'desc' : 'asc';
  const createdFrom = useMemo(() => (createdFromIso ? new Date(createdFromIso) : null), [createdFromIso]);
  const createdTo = useMemo(() => (createdToIso ? new Date(createdToIso) : null), [createdToIso]);

  const activeTab: TicketTab = answeredParam
    ? 'answered'
    : statusParam === 'New'
      ? 'new'
      : statusParam === 'Closed'
        ? 'closed'
        : statusParam === 'Open,InProgress,Resolved'
          ? 'opened'
          : 'all';

  /// Changes some of the filters. A change goes back to the first page unless it is itself a change of page.
  function update(changes: Record<string, string | null>, options?: { replace?: boolean }) {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        for (const [key, value] of Object.entries(changes)) {
          if (value === null || value === '') next.delete(key);
          else next.set(key, value);
        }
        if (!('page' in changes)) next.delete('page');
        return next;
      },
      { replace: options?.replace }
    );
  }

  // The search box is typed into freely and written to the address only once typing pauses: "Izv" is one request, not three.
  const [searchText, setSearchText] = useState(search);
  const settledSearch = useDebouncedValue(searchText, 300);
  const addressSearch = useRef(search);
  const latest = useRef({ search, update });
  latest.current = { search, update };
  useEffect(() => {
    if (settledSearch === latest.current.search) return;
    addressSearch.current = settledSearch;
    latest.current.update({ q: settledSearch }, { replace: true });
  }, [settledSearch]);
  useEffect(() => {
    // The address changed by itself (back button, a link): show it. A change this page just wrote must not overwrite what is being typed.
    if (search !== addressSearch.current) {
      addressSearch.current = search;
      setSearchText(search);
    }
  }, [search]);

  function selectTab(tab: TicketTab) {
    const p = tabToParams(tab);
    update({ status: p.status ?? null, answered: p.answered ?? null });
  }

  const effectiveAssignedToUserId = mineOnly && activeTab !== 'new' ? user?.id : assignedToUserId || undefined;

  // Each combination of filters is a list of its own: the answer to an earlier one can never be shown as the answer to the current one.
  const {
    data: loaded,
    isPending: loading,
    isError,
  } = useTicketList({
    page,
    pageSize: PAGE_SIZE,
    status: statusParam || undefined,
    answeredOnly: activeTab === 'answered',
    departmentId: departmentId || undefined,
    assignedToUserId: effectiveAssignedToUserId,
    search: search || undefined,
    createdFrom: createdFromIso ?? undefined,
    createdTo: createdToIso ?? undefined,
    sortBy: sortBy ?? undefined,
    sortDir,
  });
  const items: TicketListItem[] = loaded?.items ?? [];
  const totalCount = loaded?.totalCount ?? 0;
  const loadError = isError ? 'The tickets could not be loaded. Try again in a moment.' : null;

  const employeesByBranch = useMemo(() => {
    const scoped = departmentId ? employees.filter((e) => e.departmentId === departmentId) : employees;
    const groups = new Map<string, UserListItemDto[]>();
    for (const e of scoped) {
      const key = e.departmentName ?? 'No branch';
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(e);
    }
    return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [employees, departmentId]);

  function handleSort(key: string) {
    if (sortBy === key) update({ dir: sortDir === 'asc' ? 'desc' : 'asc' });
    else update({ sort: key, dir: 'asc' });
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const rangeLabel = createdFrom
    ? `from ${format(createdFrom, 'dd.MM.yyyy')} to ${createdTo ? format(createdTo, 'dd.MM.yyyy') : 'today'}`
    : 'all time';

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-xl font-bold text-slate-900">{user?.role === 'Client' ? 'My Tickets' : 'Tickets'}</h1>
        {user?.role === 'Client' && (
          <Link to="/tickets/new">
            <Button>Submit a ticket</Button>
          </Link>
        )}
        {isStaff && (
          <Link to="/tickets/new-on-behalf">
            <Button>New ticket</Button>
          </Link>
        )}
      </div>

      <div className="mb-4 flex gap-2 overflow-x-auto border-b border-slate-200" role="tablist" aria-label="Ticket groups">
        {TABS.map((t) => (
          <button
            key={t.key}
            role="tab"
            aria-selected={activeTab === t.key}
            onClick={() => selectTab(t.key)}
            className={`whitespace-nowrap px-3 py-2 text-sm font-medium ${activeTab === t.key ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <Card className="mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="w-full sm:w-56">
          <Input
            aria-label="Search tickets by number or title"
            placeholder="Search by number or title…"
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
          />
        </div>
        {isAdmin && (
          <div className="w-full sm:w-44">
            <Select
              aria-label="Branch"
              value={departmentId}
              onChange={(e) => update({ dept: e.target.value, assignee: null })}
            >
              <option value="">All branches</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
          </div>
        )}
        {isStaff && !mineOnly && activeTab !== 'new' && (
          <div className="w-full sm:w-52">
            <Select aria-label="Assigned employee" value={assignedToUserId} onChange={(e) => update({ assignee: e.target.value })}>
              <option value="">All employees</option>
              {employeesByBranch.map(([branchName, group]) => (
                <optgroup key={branchName} label={branchName}>
                  {group.map((e) => (
                    <option key={e.id} value={e.id}>
                      {e.firstName} {e.lastName}
                    </option>
                  ))}
                </optgroup>
              ))}
            </Select>
          </div>
        )}
        {isEmployee && activeTab !== 'new' && (
          <button
            type="button"
            aria-pressed={mineOnly}
            onClick={() => update({ mine: mineOnly ? null : '1' })}
            className={clsx(
              'rounded-full border px-3 py-2 text-xs font-medium',
              mineOnly ? 'border-blue-700 bg-blue-50 text-blue-700' : 'border-slate-300 text-slate-500 hover:border-slate-400'
            )}
          >
            Assigned to me
          </button>
        )}
        <DateRangePicker
          from={createdFrom}
          to={createdTo}
          onChange={(f, t) => update({ from: f ? f.toISOString() : null, to: t ? t.toISOString() : null })}
          clearable
        />
      </Card>

      {loadError && <ErrorText>{loadError}</ErrorText>}
      <p className="mb-2 px-1 text-xs text-slate-500" role="status">
        {totalCount} ticket{totalCount === 1 ? '' : 's'} · {rangeLabel}
      </p>

      <Card className="overflow-hidden">
        {loading ? (
          <div className="flex justify-center py-10">
            <Spinner className="h-6 w-6 text-blue-700" />
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <caption className="sr-only">Tickets</caption>
              <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
                <tr>
                  <SortableHeader colKey="ticketNumber" label="#" sortBy={sortBy} sortDir={sortDir} onSort={handleSort} />
                  <SortableHeader colKey="title" label="Title" sortBy={sortBy} sortDir={sortDir} onSort={handleSort} />
                  <SortableHeader colKey="createdAt" label="Created" sortBy={sortBy} sortDir={sortDir} onSort={handleSort} />
                  {isStaff && (
                    <th scope="col" className="whitespace-nowrap px-3 py-1.5">
                      Client
                    </th>
                  )}
                  {isStaff && <SortableHeader colKey="priority" label="Priority" sortBy={sortBy} sortDir={sortDir} onSort={handleSort} />}
                  <th scope="col" className="whitespace-nowrap px-3 py-1.5">
                    Assigned to
                  </th>
                </tr>
              </thead>
              <tbody>
                {items.map((t, i) => (
                  <tr
                    key={t.id}
                    onClick={() => navigate(`/tickets/${t.id}`)}
                    className={clsx(
                      'cursor-pointer border-t border-slate-100 transition-colors hover:bg-slate-200',
                      i % 2 === 1 && 'bg-slate-100'
                    )}
                  >
                    <td className="whitespace-nowrap px-3 py-1.5 font-medium">
                      {/* The whole row can be clicked with a mouse; this link is what the keyboard and a screen reader use. */}
                      <Link to={`/tickets/${t.id}`} onClick={(e) => e.stopPropagation()} className="text-blue-700 hover:underline">
                        #{t.ticketNumber}
                      </Link>
                    </td>
                    <td className="max-w-[220px] truncate px-3 py-1.5">{t.title}</td>
                    <td className="whitespace-nowrap px-3 py-1.5 text-slate-500">{format(new Date(t.createdAt), 'dd.MM.yyyy HH:mm')}</td>
                    {isStaff && (
                      <td className="whitespace-nowrap px-3 py-1.5">
                        <Link
                          to={`/users/${t.clientId}`}
                          onClick={(e) => e.stopPropagation()}
                          className="text-blue-700 hover:underline"
                        >
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
                    <td colSpan={isStaff ? 6 : 4} className="px-3 py-8 text-center text-slate-500">
                      No tickets found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {totalPages > 1 && (
        <nav aria-label="Pages" className="mt-4 flex items-center justify-center gap-2">
          <Button variant="secondary" disabled={page <= 1} onClick={() => update({ page: String(page - 1) })}>
            Previous
          </Button>
          <span className="text-sm text-slate-600">
            Page {page} of {totalPages}
          </span>
          <Button variant="secondary" disabled={page >= totalPages} onClick={() => update({ page: String(page + 1) })}>
            Next
          </Button>
        </nav>
      )}
    </div>
  );
}
