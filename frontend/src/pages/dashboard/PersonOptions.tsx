import type { EmployeeGroups } from './employeeGroups';

/** The people to choose from, under the name of their branch. */
export function PersonOptions({ groups }: { groups: EmployeeGroups }) {
  return (
    <>
      {groups.map(([branchName, group]) => (
        <optgroup key={branchName} label={branchName}>
          {group.map((e) => (
            <option key={e.id} value={e.id}>
              {e.firstName} {e.lastName} ({e.role})
            </option>
          ))}
        </optgroup>
      ))}
    </>
  );
}
