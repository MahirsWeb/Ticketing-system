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
  card: (p: object) => ['company-reports', 'card', p] as const,
  branchLoad: (p: object) => ['company-reports', 'branch-load', p] as const,
  closedBy: (p: CompanyReportParams) => ['company-reports', 'closed-by', p] as const,
  timeSeries: (p: CompanyReportParams) => ['company-reports', 'time-series', p] as const,
  openAge: (p: CompanyReportParams) => ['company-reports', 'open-age', p] as const,
  timeSpent: (p: CompanyReportParams) => ['company-reports', 'time-spent', p] as const,
  conversation: (p: CompanyReportParams) => ['company-reports', 'conversation', p] as const,
  rhythm: (p: CompanyReportParams) => ['company-reports', 'rhythm', p] as const,
  repeated: (p: CompanyReportParams) => ['company-reports', 'repeated-topics', p] as const,
  quiet: (p: object) => ['company-reports', 'quiet', p] as const,
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

/** The branches of ONE PERSON of a company (the card of the company's own load is `useBranchLoad`); asked for only when a person is chosen. */
export function useCompanyBranches(params: CompanyReportParams, enabled = true) {
  return useQuery({ queryKey: companyReportKeys.branches(params), queryFn: () => companyReportsApi.branches(params), enabled, ...FIGURES });
}

/** The card of a company (over all the years; the period only counts the clients that wrote in it). Asked for only when a company is chosen. */
export function useCompanyCard(range: { from?: string; to?: string }, companyId: string | undefined) {
  const params = { ...range, companyId: companyId ?? '' };
  return useQuery({ queryKey: companyReportKeys.card(params), queryFn: () => companyReportsApi.card(params), enabled: !!companyId, ...FIGURES });
}

/** The load of the branches that work for a company, in the period. */
export function useBranchLoad(range: { from?: string; to?: string }, companyId: string | undefined) {
  const params = { ...range, companyId: companyId ?? '' };
  return useQuery({ queryKey: companyReportKeys.branchLoad(params), queryFn: () => companyReportsApi.branchLoad(params), enabled: !!companyId, ...FIGURES });
}

export function useCompanyClosedBy(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.closedBy(params), queryFn: () => companyReportsApi.closedBy(params), ...FIGURES });
}

export function useCompanyTimeSeries(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.timeSeries(params), queryFn: () => companyReportsApi.timeSeries(params), ...FIGURES });
}

export function useCompanyOpenAge(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.openAge(params), queryFn: () => companyReportsApi.openAge(params), ...FIGURES });
}

export function useCompanyTimeSpent(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.timeSpent(params), queryFn: () => companyReportsApi.timeSpent(params), ...FIGURES });
}

export function useCompanyConversation(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.conversation(params), queryFn: () => companyReportsApi.conversation(params), ...FIGURES });
}

export function useCompanyRhythm(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.rhythm(params), queryFn: () => companyReportsApi.rhythm(params), ...FIGURES });
}

export function useCompanyRepeated(params: CompanyReportParams) {
  return useQuery({ queryKey: companyReportKeys.repeated(params), queryFn: () => companyReportsApi.repeatedTopics(params), ...FIGURES });
}

/** Companies gone quiet: over all companies, so only asked for while no company is chosen. */
export function useQuietCompanies(range: { from?: string; to?: string }, enabled = true) {
  return useQuery({ queryKey: companyReportKeys.quiet(range), queryFn: () => companyReportsApi.quiet(range), enabled, ...FIGURES });
}
