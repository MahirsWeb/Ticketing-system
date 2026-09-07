import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import clsx from 'clsx';
import { ticketsApi } from '../../api/tickets';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import type { DepartmentItemFull, TicketListItem, UserListItemDto } from '../../types';
import { Button, Card, Input, PriorityBadge, Select, Spinner } from '../../components/ui';
import { DateRangePicker } from '../../components/DateRangePicker';
import { useAuthStore } from '../../store/authStore';
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

export default function TicketsListPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const isEmployee = user?.role === 'Employee';
  const isStaff = isAdmin || isEmployee;
  const [items, setItems] = useState<TicketListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [searchParams, setSearchParams] = useSearchParams();
  const statusParam = searchParams.get('status') ?? '';
  const answeredParam = searchParams.get('answered') === '1';
  const mineOnly = searchParams.get('mine') === '1';
  const activeTab: TicketTab = answeredParam
    ? 'answered'
    : statusParam === 'New'
      ? 'new'
      : statusParam === 'Closed'
        ? 'closed'
        : statusParam === 'Open,InProgress,Resolved'
          ? 'opened'
          : 'all';
  const [departmentId, setDepartmentId] = useState('');
  const [assignedToUserId, setAssignedToUserId] = useState('');
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [employees, setEmployees] = useState<UserListItemDto[]>([]);
  const [search, setSearch] = useState('');
  const [createdFrom, setCreatedFrom] = useState<Date | null>(null);
  const [createdTo, setCreatedTo] = useState<Date | null>(null);
  const [sortBy, setSortBy] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!isStaff) return;
    if (isAdmin) lookupsApi.departments().then(setDepartments);
    usersApi.list({ role: 'Employee' }).then(setEmployees);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isStaff, isAdmin]);

  function selectTab(tab: TicketTab) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      const p = tabToParams(tab);
      if (p.status) next.set('status', p.status);
      else next.delete('status');
      if (p.answered) next.set('answered', p.answered);
      else next.delete('answered');
      return next;
    });
    setPage(1);
  }

  function toggleMine() {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (mineOnly) next.delete('mine');
      else next.set('mine', '1');
      return next;
    });
    setPage(1);
  }

  const effectiveAssignedToUserId = mineOnly && activeTab !== 'new' ? user?.id : assignedToUserId || undefined;

  useEffect(() => {
    setLoading(true);
    ticketsApi
      .list({
        page,
        pageSize: PAGE_SIZE,
        status: statusParam || undefined,
        answeredOnly: activeTab === 'answered',
        departmentId: departmentId || undefined,
        assignedToUserId: effectiveAssignedToUserId,
        search: search || undefined,
        createdFrom: createdFrom ? createdFrom.toISOString() : undefined,
        createdTo: createdTo ? createdTo.toISOString() : undefined,
        sortBy: sortBy ?? undefined,
        sortDir,
      })
      .then((res) => {
        setItems(res.items);
        setTotalCount(res.totalCount);
      })
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, statusParam, activeTab, departmentId, effectiveAssignedToUserId, search, createdFrom, createdTo, sortBy, sortDir]);

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
    if (sortBy === key) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(key);
      setSortDir('asc');
    }
    setPage(1);
  }

  function SortTh({ colKey, label }: { colKey: string; label: string }) {
    return (
      <th
        className="cursor-pointer select-none whitespace-nowrap px-3 py-1.5 hover:text-slate-700"
        onClick={() => handleSort(colKey)}
      >
        {label}
        {sortBy === colKey && <span className="ml-1">{sortDir === 'asc' ? '▲' : '▼'}</span>}
      </th>
    );
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

      <div className="mb-4 flex gap-2 border-b border-slate-200">
        {TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => selectTab(t.key)}
            className={`px-3 py-2 text-sm font-medium ${activeTab === t.key ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <Card className="mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="w-56">
          <Input
            placeholder="Search by number or title…"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />
        </div>
        {isAdmin && (
          <div className="w-44">
            <Select
              value={departmentId}
              onChange={(e) => {
                setDepartmentId(e.target.value);
                setAssignedToUserId('');
                setPage(1);
              }}
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
          <div className="w-52">
            <Select
              value={assignedToUserId}
              onChange={(e) => {
                setAssignedToUserId(e.target.value);
                setPage(1);
              }}
            >
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
            onClick={toggleMine}
            className={clsx(
              'rounded-full border px-3 py-2 text-xs font-medium',
              mineOnly ? 'border-blue-700 bg-blue-50 text-blue-700' : 'border-slate-300 text-slate-500 hover:border-slate-400'
            )}
          >
            Assigned to me
          </button>
        )}
        <DateRangePicker from={createdFrom} to={createdTo} onChange={(f, t) => { setCreatedFrom(f); setCreatedTo(t); setPage(1); }} clearable />
      </Card>

      <p className="mb-2 px-1 text-xs text-slate-400">
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
              <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
                <tr>
                  <SortTh colKey="ticketNumber" label="#" />
                  <SortTh colKey="title" label="Title" />
                  <SortTh colKey="createdAt" label="Created" />
                  {isStaff && <th className="whitespace-nowrap px-3 py-1.5">Client</th>}
                  {isStaff && <SortTh colKey="priority" label="Priority" />}
                  <th className="whitespace-nowrap px-3 py-1.5">Assigned to</th>
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
                    <td className="whitespace-nowrap px-3 py-1.5 font-medium text-blue-700">#{t.ticketNumber}</td>
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
                    <td colSpan={isStaff ? 6 : 4} className="px-3 py-8 text-center text-slate-400">
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
        <div className="mt-4 flex items-center justify-center gap-2">
          <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </Button>
          <span className="text-sm text-slate-600">
            Page {page} of {totalPages}
          </span>
          <Button variant="secondary" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
            Next
          </Button>
        </div>
      )}
    </div>
  );
}
