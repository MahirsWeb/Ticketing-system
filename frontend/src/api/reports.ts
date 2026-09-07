import { apiClient } from './client';
import type { BranchBreakdownEntryDto, LeaderboardEntryDto, PeriodComparisonPointDto, ReportSummaryDto, TimeSeriesPointDto, TopIssueDto } from '../types';

export interface ReportParams {
  from?: string;
  to?: string;
  companyId?: string;
  agentId?: string;
  departmentId?: string;
  subBranchId?: string;
}

export const reportsApi = {
  summary: (params: ReportParams) =>
    apiClient.get<ReportSummaryDto>('/api/reports/summary', { params }).then((r) => r.data),
  timeSeries: (params: ReportParams) =>
    apiClient.get<TimeSeriesPointDto[]>('/api/reports/timeseries', { params }).then((r) => r.data),
  leaderboard: (params: ReportParams) =>
    apiClient.get<LeaderboardEntryDto[]>('/api/reports/leaderboard', { params }).then((r) => r.data),
  byBranch: (params: { from?: string; to?: string }) =>
    apiClient.get<BranchBreakdownEntryDto[]>('/api/reports/by-branch', { params }).then((r) => r.data),
  periodComparison: (params: ReportParams & { groupBy: 'week' | 'month' | 'quarter' | 'year' }) =>
    apiClient.get<PeriodComparisonPointDto[]>('/api/reports/period-comparison', { params }).then((r) => r.data),
  topIssues: (params: ReportParams & { limit?: number }) =>
    apiClient.get<TopIssueDto[]>('/api/reports/top-issues', { params }).then((r) => r.data),
  aiInsights: (params: { from: string; to: string; companyId?: string; agentId?: string; departmentId?: string; subBranchId?: string }) =>
    apiClient.post<{ summary: string }>('/api/reports/ai-insights', params).then((r) => r.data),

  /// Triggers a browser download of the filtered ticket list as an Excel (.xlsx) workbook with a summary/chart sheet.
  exportXlsx: async (params: ReportParams) => {
    const response = await apiClient.get('/api/reports/export', { params, responseType: 'blob' });
    const disposition = response.headers['content-disposition'] as string | undefined;
    const match = disposition?.match(/filename="?([^"]+)"?/);
    const filename = match?.[1] ?? 'tickets-export.xlsx';

    const url = window.URL.createObjectURL(new Blob([response.data]));
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  },
};
