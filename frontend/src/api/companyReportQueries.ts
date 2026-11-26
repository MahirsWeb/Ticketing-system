import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { companyReportsApi, type CompanyListParams, type CompanyReportParams } from './companyReports';

/** One entry per selection, forgotten when the selection is left, like the figures of the first tab (see reportQueries.ts). */
export const companyReportKeys = {
  top: (p: object) => ['company-reports', 'top', p] as const,
  list: (p: CompanyListParams) => ['company-reports', 'list', p] as const,
  summary: (p: CompanyReportParams) => ['company-reports', 'summary', p] as const,
  clients: (p: object) => ['company-reports', 'clients', p] as const,
  topics: (p: CompanyReportParams) => ['company-reports', 'topics', p] as const,
  categories: (p: CompanyReportParams) => ['company-reports', 'categories', p] as const,
  branches: (p: CompanyReportParams) => ['company-reports', 'branches', p] as const,
  closedBy: (p: CompanyReportParams) => ['company-reports', 'closed-by', p] as const,
  timeSeries: (p: CompanyReportParams) => ['company-reports', 'time-series', p] as const,
};

const FIGURES = { gcTime: 0, placeholderData: keepPreviousData } as const;

export function useTopCompanies(range: { from?: string; to?: string }, enabled = true) {
  return useQuery({ queryKey: companyReportKeys.top(range), queryFn: () => companyReportsApi.top(range), enabled, ...FIGURES });
}

export function useCompanyList(params: CompanyListParams) {
  return useQuery({ queryKey: companyReportKeys.list(params), queryFn: () => companyReportsApi.list(params), ...FIGURES });
}

export function useCompanySummary(params: CompanyReportParams, enabled = true) {
  return useQuery({ queryKey: companyReportKeys.summary(params), queryFn: () => companyReportsApi.summary(params), enabled, ...FIGURES });
}

/** The most people asked for at once (the server's own limit): the card shows the first five and the rest on request, and the average per person needs them all. */
export const MOST_PEOPLE = 100;

/** The people of one company; asked for only once a company is chosen. */
export function useCompanyClientStats(range: { from?: string; to?: string }, companyId: string | undefined) {
  const params = { ...range, companyId: companyId ?? '', limit: MOST_PEOPLE };
  return useQuery({ queryKey: companyReportKeys.clients(params), queryFn: () => companyReportsApi.clients(params), enabled: !!companyId, ...FIGURES });
}

export function useCompanyTopics(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.topics(params), queryFn: () => companyReportsApi.topics(params), ...FIGURES });
}

export function useCompanyCategories(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.categories(params), queryFn: () => companyReportsApi.categories(params), ...FIGURES });
}

export function useCompanyBranches(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.branches(params), queryFn: () => companyReportsApi.branches(params), ...FIGURES });
}

export function useCompanyClosedBy(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.closedBy(params), queryFn: () => companyReportsApi.closedBy(params), ...FIGURES });
}

export function useCompanyTimeSeries(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.timeSeries(params), queryFn: () => companyReportsApi.timeSeries(params), ...FIGURES });
}
