import { useDepartments } from '../../api/lookupQueries';
import { ErrorText, Select } from '../../components/ui';

/** What the tasks are narrowed by: an administrator chooses a branch, an employee can ask for their own tasks only. */
export function TaskFilters({
  isAdmin,
  departmentId,
  onDepartment,
  mineOnly,
  onMineOnly,
}: {
  isAdmin: boolean;
  departmentId: string;
  onDepartment: (id: string) => void;
  mineOnly: boolean;
  onMineOnly: (mineOnly: boolean) => void;
}) {
  const departments = useDepartments({ enabled: isAdmin });
  return (
    <div className="flex flex-wrap items-center gap-2">
      {isAdmin ? (
        <div className="w-56">
          <Select aria-label="Branch" value={departmentId} onChange={(e) => onDepartment(e.target.value)}>
            <option value="">All branches</option>
            {(departments.data ?? []).map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </Select>
        </div>
      ) : (
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" checked={mineOnly} onChange={(e) => onMineOnly(e.target.checked)} />
          My tasks only
        </label>
      )}
      {departments.isError && <ErrorText>The list of branches could not be loaded.</ErrorText>}
    </div>
  );
}
