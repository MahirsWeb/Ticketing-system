import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ticketsApi } from './tickets';
import { usersApi } from './users';
import { userKeys } from './userKeys';
import type { UserListItemDto } from '../types';

export { userKeys };

export type UserPageParams = Parameters<typeof usersApi.listPaged>[0];

/** The clients of one company: asked for when the company is opened and forgotten when it is left. */
export function useCompanyClients(companyId: string) {
  return useQuery({ queryKey: userKeys.companyClients(companyId), queryFn: () => usersApi.list({ role: 'Client', companyId }), gcTime: 0 });
}

/**
 * One page of the list of accounts (clients or staff, for a search). Every page of every list is an entry of its own and is forgotten when
 * it is left (gcTime 0), so the answer to an earlier choice is never shown as the answer to the current one, and a request that has been
 * overtaken is cancelled. While the page for a new choice is on its way, `data` is still the one before it, so that the totals in the tabs
 * do not flash: `isPlaceholderData` says so, and what is shown for a page must not come from it.
 */
export function useUserPage(params: UserPageParams) {
  return useQuery({
    queryKey: userKeys.page(params),
    queryFn: ({ signal }) => usersApi.listPaged(params, signal),
    gcTime: 0,
    placeholderData: keepPreviousData,
  });
}

/**
 * The staff, for the screens that let somebody be picked (the "assigned to" filter of the ticket list, the people of the dashboard).
 * Asked again on each visit: the list is short and people are added now and then. Pass a branch (and a sub-branch of it) to get only their staff.
 */
export function useEmployees(options: { departmentId?: string; subBranchId?: string; enabled?: boolean } = {}) {
  return useQuery({
    queryKey: userKeys.employees(options.departmentId ?? null, options.subBranchId ?? null),
    queryFn: () => usersApi.list({ role: 'Employee', departmentId: options.departmentId, subBranchId: options.subBranchId }),
    enabled: options.enabled ?? true,
  });
}

/**
 * One person. Another person is another entry, so a slow answer for the previous one can never be shown as the answer for the current one,
 * and a failure is only about the person it was for. Forgotten when the page is left.
 */
export function useUser(id: string | undefined) {
  return useQuery({ queryKey: userKeys.detail(id ?? ''), queryFn: () => usersApi.getById(id!), enabled: !!id, gcTime: 0 });
}

/** The tickets a client submitted, or those a member of the staff has: the first hundred, with how many there are in all. */
export function useUserTickets(user: Pick<UserListItemDto, 'id' | 'role'>) {
  return useQuery({
    queryKey: userKeys.tickets(user.role, user.id),
    queryFn: () => ticketsApi.list(user.role === 'Client' ? { clientId: user.id, pageSize: 100 } : { assignedToUserId: user.id, pageSize: 100 }),
    gcTime: 0,
  });
}
