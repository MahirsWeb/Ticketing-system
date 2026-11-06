import { useMemo } from 'react';
import clsx from 'clsx';
import { useDepartments } from '../../api/lookupQueries';
import { useEmployees } from '../../api/userQueries';
import type { UserListItemDto } from '../../types';
import { Card, ErrorText, Input, Select } from '../../components/ui';
import { DateRangePicker } from '../../components/DateRangePicker';
import type { TicketListFilters } from './useTicketListFilters';

/**
 * The search box and the filters over the list. The branches are only asked for when the person can choose one (an administrator), and the
 * employees only for staff. A filter that is in the address but not among what can be chosen (an employee who is gone, a link written by hand)
 * is shown as chosen ("Selected employee"), not as "All employees" over a list that is narrowed; this is not said while the list is still coming.
 */
export function TicketFilterBar({ filters, isAdmin, isEmployee }: { filters: TicketListFilters; isAdmin: boolean; isEmployee: boolean }) {
  const isStaff = isAdmin || isEmployee;
  const { departmentId, assignedToUserId, mineOnly, activeTab, createdFrom, createdTo, searchText, setSearchText, update } = filters;
  const departments = useDepartments({ enabled: isAdmin });
  const employees = useEmployees({ enabled: isStaff });

  const employeesByBranch = useMemo(() => {
    const scoped = departmentId ? (employees.data ?? []).filter((e) => e.departmentId === departmentId) : (employees.data ?? []);
    const groups = new Map<string, UserListItemDto[]>();
    for (const e of scoped) {
      const key = e.departmentName ?? 'No branch';
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(e);
    }
    return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [employees.data, departmentId]);

  const unknownBranch = !!departmentId && departments.isSuccess && !departments.data.some((d) => d.id === departmentId);
  const unknownEmployee = !!assignedToUserId && employees.isSuccess && !employeesByBranch.some(([, group]) => group.some((e) => e.id === assignedToUserId));

  return (
    <Card className="mb-4 flex flex-wrap items-end gap-3 p-4">
      <div className="w-full sm:w-56">
        <Input
          aria-label="Search tickets by number or title"
          placeholder="Search by number or title…"
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
        />
      </div>
      {isAdmin && (
        <div className="w-full sm:w-44">
          <Select aria-label="Branch" value={departmentId} onChange={(e) => update({ dept: e.target.value, assignee: null })}>
            <option value="">All branches</option>
            {unknownBranch && <option value={departmentId}>Selected branch</option>}
            {(departments.data ?? []).map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </Select>
        </div>
      )}
      {isStaff && !mineOnly && activeTab !== 'new' && (
        <div className="w-full sm:w-52">
          <Select aria-label="Assigned employee" value={assignedToUserId} onChange={(e) => update({ assignee: e.target.value })}>
            <option value="">All employees</option>
            {unknownEmployee && <option value={assignedToUserId}>Selected employee</option>}
            {employeesByBranch.map(([branchName, group]) => (
              <optgroup key={branchName} label={branchName}>
                {group.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.firstName} {e.lastName}
                  </option>
                ))}
              </optgroup>
            ))}
          </Select>
        </div>
      )}
      {isEmployee && activeTab !== 'new' && (
        <button
          type="button"
          aria-pressed={mineOnly}
          onClick={() => update({ mine: mineOnly ? null : '1' })}
          className={clsx(
            'rounded-full border px-3 py-2 text-xs font-medium',
            mineOnly ? 'border-blue-700 bg-blue-50 text-blue-700' : 'border-slate-300 text-slate-600 hover:border-slate-400'
          )}
        >
          Assigned to me
        </button>
      )}
      <DateRangePicker
        from={createdFrom}
        to={createdTo}
        onChange={(f, t) => update({ from: f ? f.toISOString() : null, to: t ? t.toISOString() : null })}
        clearable
      />
      {departments.isError && (
        <div className="w-full">
          <ErrorText>The list of branches could not be loaded.</ErrorText>
        </div>
      )}
      {employees.isError && (
        <div className="w-full">
          <ErrorText>The list of employees could not be loaded.</ErrorText>
        </div>
      )}
    </Card>
  );
}
