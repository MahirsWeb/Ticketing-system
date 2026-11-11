import { useMemo, useState } from 'react';
import { subDays } from 'date-fns';
import { useDepartments } from '../../api/lookupQueries';
import { useEmployees } from '../../api/userQueries';
import { useBranchBreakdown, useLeaderboard, usePeriodComparison, useReportSummary, useTimeSeries, useTopIssues } from '../../api/reportQueries';
import { ErrorText } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { AiInsights } from './AiInsights';
import { BranchBreakdownCard } from './BranchBreakdownCard';
import { DailyTimeline } from './DailyTimeline';
import { DashboardFilters } from './DashboardFilters';
import { LeaderboardCard } from './LeaderboardCard';
import { PeriodComparison, type GroupBy } from './PeriodComparison';
import { SummaryCards } from './SummaryCards';
import { TicketsOverTime } from './TicketsOverTime';
import { TopIssuesCard } from './TopIssuesCard';
import { groupEmployeesByBranch } from './employeeGroups';
import { useLanguage } from '../../i18n/LanguageContext';

/**
 * The figures of the period: who they are for (a branch, an employee) and which days is the page's to decide, and every part below it only
 * shows what it is given. Each selection is an entry of its own in the cache, so the answer to an earlier one can never be shown as the
 * answer to the current one; while the figures of a new selection are on their way, those of the previous one stay on the screen.
 */
export default function DashboardPage() {
  const { t } = useLanguage();
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';

  const [to, setTo] = useState(() => new Date());
  const [from, setFrom] = useState(() => subDays(new Date(), 30));
  const [departmentId, setDepartmentId] = useState('');
  const [agentId, setAgentId] = useState('');
  const [groupBy, setGroupBy] = useState<GroupBy>('month');

  const { data: departments = [] } = useDepartments({ enabled: isAdmin });
  // Staff see their own branch's people; an administrator sees everybody.
  const { data: employees = [] } = useEmployees({ departmentId: isAdmin ? undefined : user?.departmentId ?? undefined });
  const people = useMemo(() => groupEmployeesByBranch(employees, departmentId, t('common_noBranch')), [employees, departmentId, t]);

  const params = { from: from.toISOString(), to: to.toISOString(), departmentId: departmentId || undefined, agentId: agentId || undefined };
  const summaryQuery = useReportSummary(params);
  const seriesQuery = useTimeSeries(params);
  const leaderboardQuery = useLeaderboard(params);
  const topIssuesQuery = useTopIssues(params);
  const branchQuery = useBranchBreakdown({ from: params.from, to: params.to }, isAdmin);
  const periodQuery = usePeriodComparison(params, groupBy);
  // A failed request is a message on the screen, not an unhandled error in the console.
  const reportError = [summaryQuery, seriesQuery, leaderboardQuery, topIssuesQuery, branchQuery, periodQuery].some((q) => q.isError)
    ? t('dash_partialFail')
    : null;

  return (
    <div className="space-y-6">
      <DashboardFilters
        isAdmin={isAdmin}
        ownBranchName={user?.departmentName}
        departments={departments}
        people={people}
        departmentId={departmentId}
        agentId={agentId}
        from={from}
        to={to}
        onBranchChange={(id) => {
          setDepartmentId(id);
          setAgentId('');
        }}
        onAgentChange={setAgentId}
        onPeriodChange={(f, t) => {
          setFrom(f ?? subDays(new Date(), 30));
          setTo(t ?? new Date());
        }}
      />

      <ErrorText>{reportError}</ErrorText>

      <SummaryCards summary={summaryQuery.data ?? null} />

      <DailyTimeline isAdmin={isAdmin} userId={user?.id} departments={departments} people={people} />

      <TicketsOverTime
        series={seriesQuery.data}
        from={from}
        to={to}
        onRangeChange={(f, t) => {
          setFrom(f);
          setTo(t);
        }}
      />

      <PeriodComparison points={periodQuery.data ?? []} groupBy={groupBy} onGroupByChange={setGroupBy} />

      <TopIssuesCard issues={topIssuesQuery.data ?? []}>
        <AiInsights from={from} to={to} departmentId={departmentId || undefined} agentId={agentId || undefined} />
      </TopIssuesCard>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {isAdmin && <BranchBreakdownCard items={branchQuery.data ?? []} />}
        <LeaderboardCard entries={leaderboardQuery.data ?? []} />
      </div>
    </div>
  );
}
