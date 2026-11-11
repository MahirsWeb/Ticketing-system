import type { EmployeeGroups } from './employeeGroups';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';

/** The people to choose from, under the name of their branch. */
export function PersonOptions({ groups }: { groups: EmployeeGroups }) {
  const { t } = useLanguage();
  return (
    <>
      {groups.map(([branchName, group]) => (
        <optgroup key={branchName} label={branchName}>
          {group.map((e) => (
            <option key={e.id} value={e.id}>
              {e.firstName} {e.lastName} ({t(`role_${e.role}` as TranslationKey)})
            </option>
          ))}
        </optgroup>
      ))}
    </>
  );
}
