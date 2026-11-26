import { apiClient } from './client';
import type {
  BranchCountDto,
  BreakdownItemDto,
  ClosedByDto,
  CompanyClientStatDto,
  CompanyListPageDto,
  CompanySummaryDto,
  ConversationDto,
  OpenAgeDto,
  QuietCompaniesDto,
  RepeatedTopicsDto,
  RhythmDto,
  TimeSpentDto,
  TimeSeriesPointDto,
  TopCompaniesDto,
} from '../types';

/** The period, and optionally one company and one person of it. A person is never asked for without the company. */
export interface CompanyReportParams {
  from?: string;
  to?: string;
  companyId?: string;
  clientId?: string;
}

export type CompanySort = 'total' | 'name' | 'previous' | 'growth' | 'avgresolution' | 'sla' | 'open' | 'share';

export interface CompanyListParams {
  from?: string;
  to?: string;
  search?: string;
  sort?: CompanySort;
  descending?: boolean;
  page?: number;
  pageSize?: number;
}

const base = '/api/reports/companies';

export const companyReportsApi = {
  top: (params: { from?: string; to?: string }) => apiClient.get<TopCompaniesDto>(`${base}/top`, { params }).then((r) => r.data),
  list: (params: CompanyListParams) => apiClient.get<CompanyListPageDto>(`${base}/list`, { params }).then((r) => r.data),
  summary: (params: CompanyReportParams) => apiClient.get<CompanySummaryDto>(`${base}/summary`, { params }).then((r) => r.data),
  clients: (params: { from?: string; to?: string; companyId: string; limit?: number }) => apiClient.get<CompanyClientStatDto[]>(`${base}/clients`, { params }).then((r) => r.data),
  topics: (params: CompanyReportParams) => apiClient.get<BreakdownItemDto[]>(`${base}/topics`, { params }).then((r) => r.data),
  categories: (params: CompanyReportParams) => apiClient.get<BreakdownItemDto[]>(`${base}/categories`, { params }).then((r) => r.data),
  branches: (params: CompanyReportParams) => apiClient.get<BranchCountDto[]>(`${base}/branches`, { params }).then((r) => r.data),
  closedBy: (params: CompanyReportParams) => apiClient.get<ClosedByDto>(`${base}/closed-by`, { params }).then((r) => r.data),
  timeSeries: (params: CompanyReportParams) => apiClient.get<TimeSeriesPointDto[]>(`${base}/timeseries`, { params }).then((r) => r.data),

  openAge: (params: CompanyReportParams) => apiClient.get<OpenAgeDto>(`${base}/open-age`, { params }).then((r) => r.data),
  timeSpent: (params: CompanyReportParams) => apiClient.get<TimeSpentDto>(`${base}/time-spent`, { params }).then((r) => r.data),
  conversation: (params: CompanyReportParams) => apiClient.get<ConversationDto>(`${base}/conversation`, { params }).then((r) => r.data),
  rhythm: (params: CompanyReportParams) => apiClient.get<RhythmDto>(`${base}/rhythm`, { params }).then((r) => r.data),
  repeatedTopics: (params: CompanyReportParams) => apiClient.get<RepeatedTopicsDto>(`${base}/repeated-topics`, { params }).then((r) => r.data),
  quiet: (params: { from?: string; to?: string }) => apiClient.get<QuietCompaniesDto>(`${base}/quiet`, { params }).then((r) => r.data),

  /// Downloads the workbook of the same figures as the screen shows for the same choice.
  exportXlsx: async (params: CompanyReportParams) => {
    const response = await apiClient.get(`${base}/export`, { params, responseType: 'blob' });
    const disposition = response.headers['content-disposition'] as string | undefined;
    const filename = disposition?.match(/filename="?([^"]+)"?/)?.[1] ?? 'company-statistics.xlsx';
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
