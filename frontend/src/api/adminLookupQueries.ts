import { useQuery } from '@tanstack/react-query';
import { lookupsApi } from './lookups';
import { lookupKeys } from './lookupKeys';

/**
 * The lists as the settings page needs them: with what is switched off as well, because that is where it is switched back on. They are
 * entries of the same cache as the lists the other screens use (under `['lookups']`), so every change made through `lookupsApi` refreshes them.
 * An administrator leaving a list and coming back gets the loading state and then the current list.
 */
const ADMIN = { gcTime: 0 } as const;

export function useDepartmentsAdmin() {
  return useQuery({ queryKey: lookupKeys.departmentsAdmin, queryFn: lookupsApi.departmentsAdmin, ...ADMIN });
}

export function useHelpTopicsAdmin() {
  return useQuery({ queryKey: lookupKeys.helpTopicsAdmin, queryFn: lookupsApi.helpTopicsAdmin, ...ADMIN });
}

export function useTicketCategoriesAdmin() {
  return useQuery({ queryKey: lookupKeys.ticketCategoriesAdmin, queryFn: lookupsApi.ticketCategoriesAdmin, ...ADMIN });
}

export function useSlaPlansAdmin() {
  return useQuery({ queryKey: lookupKeys.slaPlansAdmin, queryFn: lookupsApi.slaPlansAdmin, ...ADMIN });
}

export function useSubBranchesAdmin(departmentId: string) {
  return useQuery({ queryKey: lookupKeys.subBranchesAdmin(departmentId), queryFn: () => lookupsApi.subBranchesAdmin(departmentId), ...ADMIN });
}
