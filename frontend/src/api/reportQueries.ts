import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { reportsApi, type ReportParams } from './reports';
import { workTasksApi } from './workTasks';

/** Names of the cached figures of the dashboard; one entry per selection. */
export const reportKeys = {
  all: ['reports'] as const,
  summary: (p: ReportParams) => ['reports', 'summary', p] as const,
  timeSeries: (p: ReportParams) => ['reports', 'time-series', p] as const,
  leaderboard: (p: ReportParams) => ['reports', 'leaderboard', p] as const,
  topIssues: (p: ReportParams, limit: number) => ['reports', 'top-issues', p, limit] as const,
  byBranch: (p: { from?: string; to?: string }) => ['reports', 'by-branch', p] as const,
  periodComparison: (p: ReportParams, groupBy: string) => ['reports', 'period-comparison', p, groupBy] as const,
  gantt: (userId: string, date: string) => ['reports', 'gantt', 'person', userId, date] as const,
  ganttBranch: (departmentId: string, date: string) => ['reports', 'gantt', 'branch', departmentId, date] as const,
};

/**
 * Every figure is one entry per selection and is forgotten when the selection is left (gcTime 0): a figure is never shown for a period it
 * does not belong to. While the figures of a new selection are on their way, those of the previous one stay on the screen (so the page does
 * not flash "no tickets yet"), as it has always done; an answer to an earlier selection can never be shown as the answer to the current one,
 * because each selection is an entry of its own.
 */
const FIGURES = { gcTime: 0, placeholderData: keepPreviousData } as const;

export function useReportSummary(params: ReportParams) {
  return useQuery({ queryKey: reportKeys.summary(params), queryFn: () => reportsApi.summary(params), ...FIGURES });
}

export function useTimeSeries(params: ReportParams) {
  return useQuery({ queryKey: reportKeys.timeSeries(params), queryFn: () => reportsApi.timeSeries(params), ...FIGURES });
}

export function useLeaderboard(params: ReportParams) {
  return useQuery({ queryKey: reportKeys.leaderboard(params), queryFn: () => reportsApi.leaderboard(params), ...FIGURES });
}

export const TOP_ISSUES_LIMIT = 15;

export function useTopIssues(params: ReportParams) {
  return useQuery({ queryKey: reportKeys.topIssues(params, TOP_ISSUES_LIMIT), queryFn: () => reportsApi.topIssues({ ...params, limit: TOP_ISSUES_LIMIT }), ...FIGURES });
}

/** The breakdown by branch is only for administrators and only depends on the period. */
export function useBranchBreakdown(range: { from?: string; to?: string }, enabled: boolean) {
  return useQuery({ queryKey: reportKeys.byBranch(range), queryFn: () => reportsApi.byBranch(range), enabled, ...FIGURES });
}

export function usePeriodComparison(params: ReportParams, groupBy: 'week' | 'month' | 'quarter' | 'year') {
  return useQuery({ queryKey: reportKeys.periodComparison(params, groupBy), queryFn: () => reportsApi.periodComparison({ ...params, groupBy }), ...FIGURES });
}

/** One person's day. Nothing is asked for until somebody is chosen (or while the other view is on). */
export function useGantt(userId: string, date: string, enabled: boolean) {
  return useQuery({ queryKey: reportKeys.gantt(userId, date), queryFn: () => workTasksApi.gantt(userId, date), enabled: enabled && !!userId, gcTime: 0 });
}

/** Everybody's day in one branch. */
export function useBranchGantt(departmentId: string, date: string, enabled: boolean) {
  return useQuery({ queryKey: reportKeys.ganttBranch(departmentId, date), queryFn: () => workTasksApi.ganttBranch(departmentId, date), enabled: enabled && !!departmentId, gcTime: 0 });
}
