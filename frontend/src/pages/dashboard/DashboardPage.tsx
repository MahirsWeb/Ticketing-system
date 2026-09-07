import { useEffect, useMemo, useState } from 'react';
import { format, subDays } from 'date-fns';
import { reportsApi } from '../../api/reports';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { workTasksApi } from '../../api/workTasks';
import type {
  BranchBreakdownEntryDto,
  BranchGanttResponse,
  DepartmentItemFull,
  GanttResponse,
  LeaderboardEntryDto,
  PeriodComparisonPointDto,
  ReportSummaryDto,
  TimeSeriesPointDto,
  TopIssueDto,
  UserListItemDto,
} from '../../types';
import { Button, Card, ErrorText, Select } from '../../components/ui';
import { DateRangePicker } from '../../components/DateRangePicker';
import { GanttChart } from '../../components/GanttChart';
import { useAuthStore } from '../../store/authStore';
import { LineChart, Line, BarChart, Bar, Cell, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend, ReferenceArea } from 'recharts';

const MIN_AI_INSIGHTS_DAYS = 28;
const GROUP_BY_OPTIONS: { value: 'week' | 'month' | 'quarter' | 'year'; label: string }[] = [
  { value: 'week', label: 'By week' },
  { value: 'month', label: 'By month' },
  { value: 'quarter', label: 'By quarter' },
  { value: 'year', label: 'By year' },
];

function KpiCard({ label, value, hint, accent }: { label: string; value: string | number; hint: string; accent?: string }) {
  return (
    <Card className="p-4" title={hint}>
      <div className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-slate-400">
        {label}
        <span className="flex h-3.5 w-3.5 cursor-help items-center justify-center rounded-full border border-slate-300 text-[9px] font-bold text-slate-400">
          ?
        </span>
      </div>
      <div className={`mt-1 text-2xl font-bold ${accent ?? 'text-slate-900'}`}>{value}</div>
    </Card>
  );
}

function TopFiveList<T>({
  items,
  keyOf,
  render,
}: {
  items: T[];
  keyOf: (item: T, index: number) => string;
  render: (item: T, index: number) => React.ReactNode;
}) {
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? items : items.slice(0, 5);
  return (
    <>
      <ol className="space-y-2">
        {visible.map((item, i) => (
          <li key={keyOf(item, i)}>{render(item, i)}</li>
        ))}
      </ol>
      {items.length > 5 && (
        <button
          className="mt-3 text-xs font-medium text-blue-700 hover:underline"
          onClick={() => setShowAll((s) => !s)}
        >
          {showAll ? 'Show less' : `Show more (${items.length - 5} more)`}
        </button>
      )}
    </>
  );
}

export default function DashboardPage() {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';

  const [to, setTo] = useState(() => new Date());
  const [from, setFrom] = useState(() => subDays(new Date(), 30));
  const [departmentId, setDepartmentId] = useState('');
  const [agentId, setAgentId] = useState('');

  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [employees, setEmployees] = useState<UserListItemDto[]>([]);

  const [summary, setSummary] = useState<ReportSummaryDto | null>(null);
  const [series, setSeries] = useState<TimeSeriesPointDto[]>([]);
  const [hideWeekends, setHideWeekends] = useState(false);
  const [zoomStack, setZoomStack] = useState<{ from: Date; to: Date }[]>([]);
  const [refAreaLeft, setRefAreaLeft] = useState<string | null>(null);
  const [refAreaRight, setRefAreaRight] = useState<string | null>(null);
  const [leaderboard, setLeaderboard] = useState<LeaderboardEntryDto[]>([]);
  const [branchBreakdown, setBranchBreakdown] = useState<BranchBreakdownEntryDto[]>([]);
  const [topIssues, setTopIssues] = useState<TopIssueDto[]>([]);
  const [exporting, setExporting] = useState(false);

  const [groupBy, setGroupBy] = useState<'week' | 'month' | 'quarter' | 'year'>('month');
  const [periodMode, setPeriodMode] = useState<'count' | 'percent'>('count');
  const [periodPoints, setPeriodPoints] = useState<PeriodComparisonPointDto[]>([]);

  const [aiInsight, setAiInsight] = useState<string | null>(null);
  const [aiLoading, setAiLoading] = useState(false);
  const [aiError, setAiError] = useState<string | null>(null);

  const [ganttMode, setGanttMode] = useState<'person' | 'branch'>('person');
  const [ganttUserId, setGanttUserId] = useState('');
  const [ganttDepartmentId, setGanttDepartmentId] = useState('');
  const [ganttDate, setGanttDate] = useState(() => new Date());
  const [ganttData, setGanttData] = useState<GanttResponse | null>(null);
  const [ganttBranchData, setGanttBranchData] = useState<BranchGanttResponse | null>(null);
  const [ganttLoading, setGanttLoading] = useState(false);
  const [ganttError, setGanttError] = useState<string | null>(null);

  const rangeDays = Math.round((to.getTime() - from.getTime()) / 86400000);
  const canRunAiInsights = rangeDays >= MIN_AI_INSIGHTS_DAYS;

  useEffect(() => {
    if (isAdmin) lookupsApi.departments().then(setDepartments);
    const scope = isAdmin ? undefined : user?.departmentId ?? undefined;
    usersApi.list({ role: 'Employee', departmentId: scope }).then(setEmployees);
  }, [isAdmin, user?.departmentId]);

  // Staff default to their own timeline; Admin must pick someone.
  useEffect(() => {
    if (!isAdmin && user?.id) setGanttUserId(user.id);
  }, [isAdmin, user?.id]);

  useEffect(() => {
    if (ganttMode === 'branch') {
      if (!ganttDepartmentId) {
        setGanttBranchData(null);
        return;
      }
      setGanttLoading(true);
      setGanttError(null);
      workTasksApi
        .ganttBranch(ganttDepartmentId, format(ganttDate, 'yyyy-MM-dd'))
        .then(setGanttBranchData)
        .catch((err: any) => setGanttError(err?.response?.data?.message ?? 'Could not load the timeline.'))
        .finally(() => setGanttLoading(false));
      return;
    }
    if (!ganttUserId) {
      setGanttData(null);
      return;
    }
    setGanttLoading(true);
    setGanttError(null);
    workTasksApi
      .gantt(ganttUserId, format(ganttDate, 'yyyy-MM-dd'))
      .then(setGanttData)
      .catch((err: any) => setGanttError(err?.response?.data?.message ?? 'Could not load the timeline.'))
      .finally(() => setGanttLoading(false));
  }, [ganttMode, ganttUserId, ganttDepartmentId, ganttDate]);

  useEffect(() => {
    const params = { from: from.toISOString(), to: to.toISOString(), departmentId: departmentId || undefined, agentId: agentId || undefined };

    reportsApi.summary(params).then(setSummary);
    reportsApi.timeSeries(params).then(setSeries);
    reportsApi.leaderboard(params).then(setLeaderboard);
    reportsApi.topIssues({ ...params, limit: 15 }).then(setTopIssues);
    if (isAdmin) reportsApi.byBranch({ from: params.from, to: params.to }).then(setBranchBreakdown);
    setAiInsight(null);
    setAiError(null);
  }, [from, to, departmentId, agentId, isAdmin]);

  useEffect(() => {
    reportsApi
      .periodComparison({ from: from.toISOString(), to: to.toISOString(), departmentId: departmentId || undefined, agentId: agentId || undefined, groupBy })
      .then(setPeriodPoints);
  }, [from, to, departmentId, agentId, groupBy]);

  // Employees grouped by branch when viewing "all branches"; filtered to one branch otherwise.
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

  const timeSeriesChartData = useMemo(
    () => (hideWeekends ? series.filter((p) => ![0, 6].includes(new Date(p.date).getDay())) : series),
    [series, hideWeekends]
  );

  function handleChartMouseDown(e: any) {
    if (e?.activeLabel) setRefAreaLeft(e.activeLabel);
  }
  function handleChartMouseMove(e: any) {
    if (refAreaLeft && e?.activeLabel) setRefAreaRight(e.activeLabel);
  }
  function handleChartMouseUp() {
    if (refAreaLeft && refAreaRight) {
      let start = new Date(refAreaLeft);
      let end = new Date(refAreaRight);
      if (start.getTime() > end.getTime()) [start, end] = [end, start];
      if (start.getTime() !== end.getTime()) {
        setZoomStack((s) => [...s, { from, to }]);
        setFrom(start);
        setTo(end);
      }
    }
    setRefAreaLeft(null);
    setRefAreaRight(null);
  }
  function handleChartContextMenu(e: any) {
    e.preventDefault();
    setZoomStack((s) => {
      if (s.length === 0) return s;
      const prev = s[s.length - 1];
      setFrom(prev.from);
      setTo(prev.to);
      return s.slice(0, -1);
    });
  }

  const periodTotal = periodPoints.reduce((sum, p) => sum + p.count, 0);
  const periodChartData = periodPoints.map((p) => ({
    ...p,
    value: periodMode === 'percent' ? (periodTotal > 0 ? Math.round((p.count / periodTotal) * 1000) / 10 : 0) : p.count,
  }));
  const busiest = periodPoints.length > 0 ? periodPoints.reduce((a, b) => (b.count > a.count ? b : a)) : null;
  const quietest = periodPoints.length > 0 ? periodPoints.reduce((a, b) => (b.count < a.count ? b : a)) : null;

  async function handleExport() {
    setExporting(true);
    try {
      await reportsApi.exportXlsx({ from: from.toISOString(), to: to.toISOString(), departmentId: departmentId || undefined, agentId: agentId || undefined });
    } finally {
      setExporting(false);
    }
  }

  async function handleAiInsights() {
    setAiLoading(true);
    setAiError(null);
    try {
      const res = await reportsApi.aiInsights({ from: from.toISOString(), to: to.toISOString(), departmentId: departmentId || undefined, agentId: agentId || undefined });
      setAiInsight(res.summary);
    } catch (err: any) {
      setAiError(err?.response?.data?.message ?? 'Could not generate AI insights.');
    } finally {
      setAiLoading(false);
    }
  }

  return (
    <div className="space-y-6">
      <div className="sticky top-0 z-20 -mx-4 flex flex-wrap items-center justify-between gap-3 border-b border-slate-300 bg-slate-200 px-4 py-3">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Dashboard</h1>
          {!isAdmin && user?.departmentName && (
            <p className="text-xs text-slate-400">Showing data for your branch: {user.departmentName}</p>
          )}
          {isAdmin && <p className="text-xs text-slate-400">Global statistics across all branches</p>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {isAdmin && (
            <div className="w-48">
              <Select value={departmentId} onChange={(e) => { setDepartmentId(e.target.value); setAgentId(''); }}>
                <option value="">All branches</option>
                {departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </Select>
            </div>
          )}
          {isAdmin && (
            <div className="w-56">
              <Select value={agentId} onChange={(e) => setAgentId(e.target.value)}>
                <option value="">All employees</option>
                {employeesByBranch.map(([branchName, group]) => (
                  <optgroup key={branchName} label={branchName}>
                    {group.map((e) => (
                      <option key={e.id} value={e.id}>
                        {e.firstName} {e.lastName} ({e.role})
                      </option>
                    ))}
                  </optgroup>
                ))}
              </Select>
            </div>
          )}
          <DateRangePicker
            from={from}
            to={to}
            onChange={(f, t) => {
              setFrom(f ?? subDays(new Date(), 30));
              setTo(t ?? new Date());
            }}
          />
          <Button variant="secondary" onClick={handleExport} disabled={exporting}>
            {exporting ? 'Exporting…' : '⬇ Export XLSX'}
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 md:grid-cols-5">
        <KpiCard label="New" value={summary?.totalNew ?? '—'} hint="Tickets submitted but not yet opened/routed to a branch." />
        <KpiCard
          label="Open / In progress"
          value={summary?.totalOpen ?? '—'}
          accent="text-amber-600"
          hint="Tickets that have been opened and assigned, but not yet closed."
        />
        <KpiCard label="Closed" value={summary?.totalClosed ?? '—'} accent="text-green-700" hint="Tickets resolved and closed in this period." />
        <KpiCard
          label="Avg. resolution (h)"
          value={summary?.avgResolutionHours ?? '—'}
          hint="Average time between a ticket being opened and being closed, in hours."
        />
        <KpiCard
          label="SLA compliance"
          value={summary ? `${summary.slaComplianceRate}%` : '—'}
          accent="text-blue-700"
          hint="Share of closed tickets (with a due date) that were resolved by their SLA due date."
        />
      </div>

      <Card className="p-5">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-sm font-bold uppercase tracking-wide text-slate-500">Daily timeline</h2>
            <p className="text-xs text-slate-400">Tickets and tasks for one person, on the same hour axis — see what was squeezed in alongside a ticket.</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {isAdmin && (
              <div className="flex rounded-md border border-slate-300 p-0.5 text-xs">
                <button
                  className={`rounded px-2.5 py-1 font-medium ${ganttMode === 'person' ? 'bg-blue-700 text-white' : 'text-slate-500'}`}
                  onClick={() => setGanttMode('person')}
                >
                  One person
                </button>
                <button
                  className={`rounded px-2.5 py-1 font-medium ${ganttMode === 'branch' ? 'bg-blue-700 text-white' : 'text-slate-500'}`}
                  onClick={() => setGanttMode('branch')}
                >
                  Whole branch
                </button>
              </div>
            )}
            {ganttMode === 'person' ? (
              <div className="w-72">
                <Select value={ganttUserId} onChange={(e) => setGanttUserId(e.target.value)}>
                  <option value="">Select a person…</option>
                  {employeesByBranch.map(([branchName, group]) => (
                    <optgroup key={branchName} label={branchName}>
                      {group.map((e) => (
                        <option key={e.id} value={e.id}>
                          {e.firstName} {e.lastName} ({e.role})
                        </option>
                      ))}
                    </optgroup>
                  ))}
                </Select>
              </div>
            ) : (
              <div className="w-56">
                <Select value={ganttDepartmentId} onChange={(e) => setGanttDepartmentId(e.target.value)}>
                  <option value="">Select a branch…</option>
                  {departments.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </Select>
              </div>
            )}
            <input
              type="date"
              value={format(ganttDate, 'yyyy-MM-dd')}
              onChange={(e) => e.target.value && setGanttDate(new Date(`${e.target.value}T00:00:00`))}
              className="rounded-md border border-slate-300 px-3 py-2 text-sm"
            />
            <Button variant="secondary" onClick={() => setGanttDate(new Date())}>
              Today
            </Button>
          </div>
        </div>
        {ganttMode === 'branch' ? (
          !ganttDepartmentId ? (
            <p className="py-10 text-center text-sm text-slate-400">Choose a branch above to see everyone's timeline.</p>
          ) : ganttLoading ? (
            <p className="py-10 text-center text-sm text-slate-400">Loading…</p>
          ) : ganttError ? (
            <ErrorText>{ganttError}</ErrorText>
          ) : !ganttBranchData || ganttBranchData.users.length === 0 ? (
            <p className="py-10 text-center text-sm text-slate-400">No active employees in this branch.</p>
          ) : (
            <div className="space-y-5">
              {ganttBranchData.users.map((u) => (
                <div key={u.userId} className="border-t border-slate-100 pt-3 first:border-t-0 first:pt-0">
                  <div className="mb-1 text-xs font-semibold text-slate-600">{u.userName}</div>
                  <GanttChart entries={u.entries} date={ganttDate} />
                </div>
              ))}
            </div>
          )
        ) : !ganttUserId ? (
          <p className="py-10 text-center text-sm text-slate-400">Choose a person above to see their timeline.</p>
        ) : ganttLoading ? (
          <p className="py-10 text-center text-sm text-slate-400">Loading…</p>
        ) : ganttError ? (
          <ErrorText>{ganttError}</ErrorText>
        ) : (
          <GanttChart entries={ganttData?.entries ?? []} date={ganttDate} />
        )}
      </Card>

      <Card className="p-5">
        <div className="mb-1 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold uppercase tracking-wide text-slate-500">Tickets over time</h2>
          <div className="flex items-center gap-3">
            {zoomStack.length > 0 && (
              <button
                type="button"
                onClick={() => {
                  const prev = zoomStack[0];
                  setFrom(prev.from);
                  setTo(prev.to);
                  setZoomStack([]);
                }}
                className="text-xs text-slate-400 hover:text-slate-600 hover:underline"
              >
                Reset zoom
              </button>
            )}
            <label className="flex items-center gap-1.5 text-xs text-slate-500">
              <input type="checkbox" checked={hideWeekends} onChange={(e) => setHideWeekends(e.target.checked)} />
              Hide weekends
            </label>
          </div>
        </div>
        <p className="mb-3 text-xs text-slate-400">Drag to zoom into a range · right-click to zoom back out</p>
        <div onContextMenu={handleChartContextMenu} className="select-none">
          <ResponsiveContainer width="100%" height={280}>
            <LineChart
              data={timeSeriesChartData}
              onMouseDown={handleChartMouseDown}
              onMouseMove={handleChartMouseMove}
              onMouseUp={handleChartMouseUp}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis dataKey="date" tick={{ fontSize: 12 }} tickFormatter={(d) => format(new Date(d), 'dd MMM')} allowDataOverflow />
              <YAxis tick={{ fontSize: 12 }} allowDecimals={false} />
              <Tooltip labelFormatter={(d) => (d ? format(new Date(d as string), 'dd MMM yyyy') : '')} />
              <Legend />
              <Line type="monotone" dataKey="opened" name="Opened" stroke="#2f5ea8" strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="closed" name="Closed" stroke="#15803d" strokeWidth={2} dot={false} />
              {refAreaLeft && refAreaRight && (
                <ReferenceArea x1={refAreaLeft} x2={refAreaRight} strokeOpacity={0.3} fill="#2f5ea8" fillOpacity={0.15} />
              )}
            </LineChart>
          </ResponsiveContainer>
        </div>
      </Card>

      <Card className="p-5">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold uppercase tracking-wide text-slate-500">Period comparison</h2>
          <div className="flex items-center gap-2">
            <div className="w-36">
              <Select value={groupBy} onChange={(e) => setGroupBy(e.target.value as any)}>
                {GROUP_BY_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </Select>
            </div>
            <div className="flex rounded-md border border-slate-300 text-xs">
              <button
                onClick={() => setPeriodMode('count')}
                className={`px-3 py-1.5 rounded-l-md ${periodMode === 'count' ? 'bg-blue-700 text-white' : 'text-slate-600'}`}
              >
                Count
              </button>
              <button
                onClick={() => setPeriodMode('percent')}
                className={`px-3 py-1.5 rounded-r-md ${periodMode === 'percent' ? 'bg-blue-700 text-white' : 'text-slate-600'}`}
              >
                %
              </button>
            </div>
          </div>
        </div>
        <ResponsiveContainer width="100%" height={260}>
          <BarChart data={periodChartData}>
            <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
            <XAxis dataKey="label" tick={{ fontSize: 11 }} />
            <YAxis tick={{ fontSize: 12 }} unit={periodMode === 'percent' ? '%' : ''} allowDecimals={periodMode === 'percent'} />
            <Tooltip formatter={(v: any) => (periodMode === 'percent' ? `${v}%` : v)} />
            <Bar dataKey="value" radius={[4, 4, 0, 0]}>
              {periodChartData.map((p) => (
                <Cell
                  key={p.label}
                  fill={busiest && p.label === busiest.label ? '#dc2626' : quietest && p.label === quietest.label ? '#15803d' : '#2f5ea8'}
                />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
        {busiest && quietest && (
          <p className="mt-3 text-xs text-slate-500">
            <span className="font-semibold text-red-600">Busiest: {busiest.label}</span> ({busiest.count} tickets) ·{' '}
            <span className="font-semibold text-green-700">Quietest: {quietest.label}</span> ({quietest.count} tickets)
          </p>
        )}
      </Card>

      <Card className="p-5">
        <h2 className="mb-1 text-sm font-bold uppercase tracking-wide text-slate-500">Top issues (by help topic)</h2>
        <p className="mb-4 text-xs text-slate-400">What's causing the most tickets in this period.</p>
        {topIssues.length === 0 ? (
          <p className="text-sm text-slate-400">No routed tickets in this period yet.</p>
        ) : (
          <ResponsiveContainer width="100%" height={Math.max(120, topIssues.length * 34)}>
            <BarChart data={topIssues} layout="vertical" margin={{ left: 24 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis type="number" allowDecimals={false} tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="helpTopicName" width={160} tick={{ fontSize: 12 }} />
              <Tooltip />
              <Bar dataKey="count" fill="#2f5ea8" radius={[0, 4, 4, 0]} />
            </BarChart>
          </ResponsiveContainer>
        )}

        <div className="mt-5 border-t border-slate-100 pt-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h3 className="text-sm font-semibold text-slate-700">AI insights</h3>
              <p className="text-xs text-slate-400">
                {canRunAiInsights
                  ? 'Ask AI to summarize what went wrong most in the selected period and what to do about it.'
                  : `Select a period of at least ${MIN_AI_INSIGHTS_DAYS} days to run AI analysis.`}
              </p>
            </div>
            <Button variant="secondary" onClick={handleAiInsights} disabled={!canRunAiInsights || aiLoading}>
              {aiLoading ? 'Analyzing…' : '✨ Generate AI insights'}
            </Button>
          </div>
          {aiError && <p className="mt-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{aiError}</p>}
          {aiInsight && <p className="mt-3 whitespace-pre-wrap rounded-md bg-blue-50 px-3 py-2 text-sm text-blue-900">{aiInsight}</p>}
        </div>
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {isAdmin && (
          <Card className="p-5">
            <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">By branch</h2>
            {branchBreakdown.length === 0 ? (
              <p className="text-sm text-slate-400">No routed tickets in this period yet.</p>
            ) : (
              <TopFiveList
                items={branchBreakdown}
                keyOf={(b) => b.departmentId}
                render={(b) => (
                  <div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
                    <span className="font-medium text-slate-800">{b.departmentName}</span>
                    <span className="flex gap-3 text-xs text-slate-500">
                      <span>{b.totalNew} new</span>
                      <span className="text-amber-600">{b.totalOpen} open</span>
                      <span className="text-green-700">{b.totalClosed} closed</span>
                    </span>
                  </div>
                )}
              />
            )}
          </Card>
        )}

        <Card className="p-5">
          <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">Top agents (closed tickets)</h2>
          {leaderboard.length === 0 ? (
            <p className="text-sm text-slate-400">No closed tickets in this period yet.</p>
          ) : (
            <TopFiveList
              items={leaderboard}
              keyOf={(entry) => entry.userId}
              render={(entry, i) => (
                <div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
                  <span>
                    <span className="mr-2 font-bold text-slate-400">#{i + 1}</span>
                    {entry.name}
                  </span>
                  <span className="font-semibold text-slate-800">{entry.closedCount} closed</span>
                </div>
              )}
            />
          )}
        </Card>
      </div>
    </div>
  );
}
