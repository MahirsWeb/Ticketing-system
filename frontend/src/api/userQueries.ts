import { useQuery } from '@tanstack/react-query';
import { usersApi } from './users';

/**
 * The staff, for the screens that let somebody be picked (the "assigned to" filter of the ticket list, the people of the dashboard).
 * Asked again on each visit: the list is short and people are added now and then. Pass a branch to get only the staff of that branch.
 */
export function useEmployees(options: { departmentId?: string; enabled?: boolean } = {}) {
  return useQuery({
    queryKey: ['users', 'employees', options.departmentId ?? null],
    queryFn: () => usersApi.list({ role: 'Employee', departmentId: options.departmentId }),
    enabled: options.enabled ?? true,
  });
}
