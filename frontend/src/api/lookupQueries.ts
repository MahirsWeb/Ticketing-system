import { useQuery, useQueryClient } from '@tanstack/react-query';
import { lookupsApi } from './lookups';

/**
 * The lists the application is configured with (branches, firms, help topics, SLA plans, categories, sub-branches) are asked for by a
 * dozen screens and change a few times a year. They are fetched once and shared, instead of by every screen on every visit; the settings
 * screens, which change them, call {@link useRefreshLookups} so nobody keeps looking at an old list.
 */
export const lookupKeys = {
  all: ['lookups'] as const,
  departments: ['lookups', 'departments'] as const,
  companies: ['lookups', 'companies'] as const,
  helpTopics: ['lookups', 'help-topics'] as const,
  slaPlans: ['lookups', 'sla-plans'] as const,
  ticketCategories: ['lookups', 'ticket-categories'] as const,
  subBranches: (departmentId: string) => ['lookups', 'sub-branches', departmentId] as const,
};

/** Some screens need a list only for some people (the branch filter is for Admin) or only once something is opened. */
export interface LookupOptions {
  enabled?: boolean;
}

/** For how long a fetched list is used as it is. */
export const LOOKUP_STALE_MS = 5 * 60 * 1000;

export function useDepartments(options: LookupOptions = {}) {
  return useQuery({ queryKey: lookupKeys.departments, queryFn: lookupsApi.departments, staleTime: LOOKUP_STALE_MS, enabled: options.enabled ?? true });
}

export function useCompanies(options: LookupOptions = {}) {
  return useQuery({ queryKey: lookupKeys.companies, queryFn: lookupsApi.companies, staleTime: LOOKUP_STALE_MS, enabled: options.enabled ?? true });
}

export function useHelpTopics(options: LookupOptions = {}) {
  return useQuery({ queryKey: lookupKeys.helpTopics, queryFn: lookupsApi.helpTopics, staleTime: LOOKUP_STALE_MS, enabled: options.enabled ?? true });
}

export function useSlaPlans(options: LookupOptions = {}) {
  return useQuery({ queryKey: lookupKeys.slaPlans, queryFn: lookupsApi.slaPlans, staleTime: LOOKUP_STALE_MS, enabled: options.enabled ?? true });
}

export function useTicketCategories(options: LookupOptions = {}) {
  return useQuery({ queryKey: lookupKeys.ticketCategories, queryFn: lookupsApi.ticketCategories, staleTime: LOOKUP_STALE_MS, enabled: options.enabled ?? true });
}

/** The sub-branches of one branch; nothing is asked for while no branch is chosen. */
export function useSubBranches(departmentId: string | null | undefined, options: LookupOptions = {}) {
  return useQuery({
    queryKey: lookupKeys.subBranches(departmentId ?? ''),
    queryFn: () => lookupsApi.subBranches(departmentId!),
    enabled: !!departmentId && (options.enabled ?? true),
    staleTime: LOOKUP_STALE_MS,
  });
}

/** Call after one of the lists was changed: every screen that uses a list gets the new one the next time it looks. */
export function useRefreshLookups() {
  const client = useQueryClient();
  return () => client.invalidateQueries({ queryKey: lookupKeys.all });
}
