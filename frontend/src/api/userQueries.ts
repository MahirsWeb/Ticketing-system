import { useQuery } from '@tanstack/react-query';
import { ticketsApi } from './tickets';
import { usersApi } from './users';
import type { UserListItemDto } from '../types';

export const userKeys = {
  all: ['users'] as const,
  employees: (departmentId: string | null) => ['users', 'employees', departmentId] as const,
  detail: (id: string) => ['users', 'detail', id] as const,
  tickets: (role: string, id: string) => ['users', 'tickets', role, id] as const,
};

/**
 * The staff, for the screens that let somebody be picked (the "assigned to" filter of the ticket list, the people of the dashboard).
 * Asked again on each visit: the list is short and people are added now and then. Pass a branch to get only the staff of that branch.
 */
export function useEmployees(options: { departmentId?: string; enabled?: boolean } = {}) {
  return useQuery({
    queryKey: userKeys.employees(options.departmentId ?? null),
    queryFn: () => usersApi.list({ role: 'Employee', departmentId: options.departmentId }),
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
