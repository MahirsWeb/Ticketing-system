import { Link } from 'react-router-dom';
import clsx from 'clsx';
import { Card, StatusPill } from '../../components/ui';
import type { UserListItemDto } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';

/** The accounts of one page. The last column is the branch for the staff and the company for a client. `message` is what is said when there are no rows, or nothing when there is nothing to say. */
export function AccountsTable({ rows, staff, message }: { rows: UserListItemDto[]; staff: boolean; message: string | null }) {
  const { t } = useLanguage();
  return (
    <Card className="overflow-hidden">
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-600">
          <tr>
            <th scope="col" className="px-4 py-2">
              Name
            </th>
            <th scope="col" className="px-4 py-2">
              Email
            </th>
            <th scope="col" className="px-4 py-2">
              {staff ? t('detail_branch') : t('common_company')}
            </th>
            <th scope="col" className="px-4 py-2">
              Status
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((u, i) => (
            <tr key={u.id} className={clsx('border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}>
              <td className="px-4 py-2">
                <Link to={`/users/${u.id}`} className="font-medium text-blue-700 hover:underline">
                  {u.firstName} {u.lastName}
                </Link>
              </td>
              <td className="px-4 py-2">{u.email}</td>
              <td className="px-4 py-2 text-slate-600">{(staff ? u.departmentName : u.companyName) ?? '—'}</td>
              <td className="px-4 py-2">
                <StatusPill active={u.isActive} />
              </td>
            </tr>
          ))}
          {rows.length === 0 && message && (
            <tr>
              <td colSpan={4} className="px-4 py-8 text-center text-slate-600">
                {message}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </Card>
  );
}
