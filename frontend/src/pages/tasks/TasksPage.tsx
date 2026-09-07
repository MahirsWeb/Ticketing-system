import { Fragment, useEffect, useMemo, useState } from 'react';
import { format } from 'date-fns';
import clsx from 'clsx';
import { workTasksApi } from '../../api/workTasks';
import { usersApi } from '../../api/users';
import { lookupsApi } from '../../api/lookups';
import { useAuthStore } from '../../store/authStore';
import type { DepartmentItemFull, WorkTaskDetailDto, WorkTaskListItem, WorkTaskStatus } from '../../types';
import { Button, Card, ErrorText, Input, Label, Select, Textarea } from '../../components/ui';
import { TaskDetailPanel } from './TaskDetailPanel';

const STATUS_STYLES: Record<string, string> = {
  Pending: 'bg-slate-100 text-slate-600',
  InProgress: 'bg-blue-100 text-blue-800',
  Done: 'bg-green-100 text-green-800',
};

function TaskStatusBadge({ status }: { status: string }) {
  return (
    <span className={clsx('rounded-full px-2.5 py-0.5 text-xs font-semibold', STATUS_STYLES[status] ?? 'bg-slate-100 text-slate-600')}>
      {status}
    </span>
  );
}

const STATUS_TABS: { key: WorkTaskStatus; label: string }[] = [
  { key: 'Pending', label: 'Opened (Pending)' },
  { key: 'InProgress', label: 'In Progress' },
  { key: 'Done', label: 'Done' },
];

const SORTABLE_COLUMNS_BEFORE_STATUS: { key: string; label: string }[] = [{ key: 'title', label: 'Title' }];
const SORTABLE_COLUMNS_AFTER_STATUS: { key: string; label: string; adminOnly?: boolean }[] = [
  { key: 'assignedTo', label: 'Assigned to' },
  { key: 'assignedBy', label: 'Assigned by' },
  { key: 'branch', label: 'Branch', adminOnly: true },
  { key: 'started', label: 'Started' },
  { key: 'ended', label: 'Ended' },
];

export default function TasksPage() {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const canManage = user?.role === 'Employee';
  const canCreate = canManage || isAdmin;

  const [tasks, setTasks] = useState<WorkTaskListItem[]>([]);
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [departmentFilter, setDepartmentFilter] = useState('');
  const [mineOnly, setMineOnly] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [expandedDetail, setExpandedDetail] = useState<WorkTaskDetailDto | null>(null);
  const [expandError, setExpandError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<WorkTaskStatus>('Pending');
  const [sortBy, setSortBy] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  const [colleagues, setColleagues] = useState<{ id: string; name: string; departmentName: string | null }[]>([]);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [assignedToUserId, setAssignedToUserId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  function refresh() {
    workTasksApi
      .list({
        departmentId: isAdmin ? departmentFilter || undefined : undefined,
        assignedToUserId: mineOnly ? user?.id : undefined,
      })
      .then(setTasks);
  }

  useEffect(() => {
    if (isAdmin) lookupsApi.departments().then(setDepartments);
  }, [isAdmin]);

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [departmentFilter, mineOnly]);

  useEffect(() => {
    if (!canCreate) return;
    const scope = isAdmin ? undefined : user?.departmentId ?? undefined;
    if (!isAdmin && !scope) return;
    usersApi.list({ role: 'Employee', departmentId: scope }).then((employees) =>
      setColleagues(employees.map((u) => ({ id: u.id, name: `${u.firstName} ${u.lastName}`, departmentName: u.departmentName })))
    );
  }, [canCreate, isAdmin, user?.departmentId]);

  const colleaguesByBranch = useMemo(() => {
    const groups = new Map<string, { id: string; name: string; departmentName: string | null }[]>();
    for (const c of colleagues) {
      const key = c.departmentName ?? 'No branch';
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(c);
    }
    return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [colleagues]);

  const tasksByStatus = useMemo(() => tasks.filter((t) => t.status === statusFilter), [tasks, statusFilter]);

  function handleSort(key: string) {
    if (sortBy === key) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(key);
      setSortDir('asc');
    }
  }

  const sortedTasks = useMemo(() => {
    if (!sortBy) return tasksByStatus;
    const dir = sortDir === 'asc' ? 1 : -1;
    const valueOf = (t: WorkTaskListItem): string => {
      switch (sortBy) {
        case 'title':
          return t.title.toLowerCase();
        case 'assignedTo':
          return t.assignedToName.toLowerCase();
        case 'assignedBy':
          return t.assignedByName.toLowerCase();
        case 'branch':
          return t.departmentName.toLowerCase();
        case 'started':
          return t.startedAtUtc ?? '';
        case 'ended':
          return t.endedAtUtc ?? '';
        default:
          return '';
      }
    };
    return [...tasksByStatus].sort((a, b) => {
      const av = valueOf(a);
      const bv = valueOf(b);
      if (av < bv) return -1 * dir;
      if (av > bv) return 1 * dir;
      return 0;
    });
  }, [tasksByStatus, sortBy, sortDir]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!title.trim() || !description.trim() || !assignedToUserId) {
      setError('Title, description and assignee are all required.');
      return;
    }
    setLoading(true);
    try {
      await workTasksApi.create(title, description, assignedToUserId);
      setTitle('');
      setDescription('');
      setAssignedToUserId('');
      refresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not create the task.');
    } finally {
      setLoading(false);
    }
  }

  async function toggleExpand(id: string) {
    if (expandedId === id) {
      setExpandedId(null);
      setExpandedDetail(null);
      return;
    }
    setExpandedId(id);
    setExpandedDetail(null);
    setExpandError(null);
    try {
      setExpandedDetail(await workTasksApi.getById(id));
    } catch (err: any) {
      setExpandError(err?.response?.data?.message ?? 'Could not load this task.');
    }
  }

  function handleDetailChanged(updated: WorkTaskDetailDto) {
    setExpandedDetail(updated);
    refresh();
  }

  const colSpan = isAdmin ? 7 : 6;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Tasks</h1>
        <p className="mt-1 text-sm text-slate-500">
          {isAdmin
            ? 'You can create and assign tasks into any branch. Otherwise you only have read-only insight — reassigning or timing an existing task stays with the staff involved.'
            : 'Quick, informal work items you and your colleagues hand to each other alongside ticket work.'}
        </p>
      </div>

      {canCreate && (
        <Card className="p-5">
          <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">New task</h2>
          <form onSubmit={handleCreate} className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div>
              <Label>Title</Label>
              <Input value={title} maxLength={150} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Napravi plan za iduću sedmicu" />
            </div>
            <div>
              <Label>Assign to</Label>
              <Select value={assignedToUserId} onChange={(e) => setAssignedToUserId(e.target.value)}>
                <option value="">Choose a colleague{isAdmin ? '' : ' (or yourself)'}…</option>
                {colleaguesByBranch.map(([branchName, group]) => (
                  <optgroup key={branchName} label={branchName}>
                    {group.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                        {c.id === user?.id ? ' — me' : ''}
                      </option>
                    ))}
                  </optgroup>
                ))}
              </Select>
            </div>
            <div className="md:col-span-2">
              <Label>Description</Label>
              <Textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="What needs to be done?" />
            </div>
            <div className="md:col-span-2">
              <ErrorText>{error}</ErrorText>
              <Button type="submit" disabled={loading}>
                {loading ? 'Creating…' : 'Create task'}
              </Button>
            </div>
          </form>
        </Card>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {isAdmin ? (
          <div className="w-56">
            <Select value={departmentFilter} onChange={(e) => setDepartmentFilter(e.target.value)}>
              <option value="">All branches</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
          </div>
        ) : (
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input type="checkbox" checked={mineOnly} onChange={(e) => setMineOnly(e.target.checked)} />
            My tasks only
          </label>
        )}
      </div>

      <div className="flex gap-2 border-b border-slate-200">
        {STATUS_TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => setStatusFilter(t.key)}
            className={`px-3 py-2 text-sm font-medium ${statusFilter === t.key ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
          >
            {t.label} ({tasks.filter((x) => x.status === t.key).length})
          </button>
        ))}
      </div>

      <Card className="overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
            <tr>
              {SORTABLE_COLUMNS_BEFORE_STATUS.map((col) => (
                <th key={col.key} className="cursor-pointer select-none px-4 py-2 hover:text-slate-700" onClick={() => handleSort(col.key)}>
                  {col.label}
                  {sortBy === col.key && <span className="ml-1">{sortDir === 'asc' ? '▲' : '▼'}</span>}
                </th>
              ))}
              <th className="px-4 py-2">Status</th>
              {SORTABLE_COLUMNS_AFTER_STATUS.filter((col) => !col.adminOnly || isAdmin).map((col) => (
                <th key={col.key} className="cursor-pointer select-none px-4 py-2 hover:text-slate-700" onClick={() => handleSort(col.key)}>
                  {col.label}
                  {sortBy === col.key && <span className="ml-1">{sortDir === 'asc' ? '▲' : '▼'}</span>}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {sortedTasks.map((t, i) => (
              <Fragment key={t.id}>
                <tr
                  className={clsx('cursor-pointer border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}
                  onClick={() => toggleExpand(t.id)}
                >
                  <td className="px-4 py-2 font-medium text-slate-800">{t.title}</td>
                  <td className="px-4 py-2">
                    <TaskStatusBadge status={t.status} />
                  </td>
                  <td className="px-4 py-2">{t.assignedToName}</td>
                  <td className="px-4 py-2">{t.assignedByName}</td>
                  {isAdmin && <td className="px-4 py-2">{t.departmentName}</td>}
                  <td className="px-4 py-2">{t.startedAtUtc ? format(new Date(t.startedAtUtc), 'dd.MM HH:mm') : '—'}</td>
                  <td className="px-4 py-2">{t.endedAtUtc ? format(new Date(t.endedAtUtc), 'dd.MM HH:mm') : '—'}</td>
                </tr>
                {expandedId === t.id && expandedDetail && (
                  <tr>
                    <td colSpan={colSpan} className="p-0">
                      <TaskDetailPanel task={expandedDetail} currentUserId={user?.id} canManage={canManage} onChanged={handleDetailChanged} />
                    </td>
                  </tr>
                )}
                {expandedId === t.id && expandError && (
                  <tr>
                    <td colSpan={colSpan} className="px-4 py-3">
                      <ErrorText>{expandError}</ErrorText>
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
            {sortedTasks.length === 0 && (
              <tr>
                <td colSpan={colSpan} className="px-4 py-8 text-center text-slate-400">
                  No tasks in this status yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </Card>
    </div>
  );
}
