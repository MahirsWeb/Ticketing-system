import type { UserListItemDto } from '../../types';

export type EmployeeGroups = [string, UserListItemDto[]][];

/** Employees grouped by branch when viewing "all branches"; only the staff of the chosen branch otherwise. The groups are in the order of the branch names. */
export function groupEmployeesByBranch(employees: UserListItemDto[], departmentId: string): EmployeeGroups {
  const scoped = departmentId ? employees.filter((e) => e.departmentId === departmentId) : employees;
  const groups = new Map<string, UserListItemDto[]>();
  for (const e of scoped) {
    const key = e.departmentName ?? 'No branch';
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key)!.push(e);
  }
  return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
}
