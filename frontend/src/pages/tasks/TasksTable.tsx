import { Fragment } from 'react';
import { format } from 'date-fns';
import clsx from 'clsx';
import { Card } from '../../components/ui';
import type { WorkTaskDetailDto, WorkTaskListItem } from '../../types';
import { SORTABLE_AFTER_STATUS, SORTABLE_BEFORE_STATUS } from './taskSorting';
import { TaskRowDetail } from './TaskRowDetail';
import { TaskStatusBadge } from './TaskStatusBadge';

type Sorting = { sortBy: string | null; sortDir: 'asc' | 'desc'; onSort: (key: string) => void };

/// A sortable column heading. A real button, so it can be reached and used with the keyboard, and `aria-sort` tells a screen
/// reader which column the list is sorted by.
function SortableHeader({ colKey, label, sortBy, sortDir, onSort }: Sorting & { colKey: string; label: string }) {
  const active = sortBy === colKey;
  return (
    <th scope="col" className="px-4 py-2" aria-sort={active ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" className="cursor-pointer select-none uppercase hover:text-slate-700" onClick={() => onSort(colKey)}>
        {label}
        {active && (
          <span aria-hidden className="ml-1">
            {sortDir === 'asc' ? '▲' : '▼'}
          </span>
        )}
      </button>
    </th>
  );
}

const when = (iso: string | null) => (iso ? format(new Date(iso), 'dd.MM HH:mm') : '—');

/**
 * The tasks of one status, a row each; the one that is open has its detail under it. `message` is what the table says when it has no rows
 * (that there are none, or that they are on their way), and nothing when it has none to say.
 */
export function TasksTable({
  tasks,
  message,
  isAdmin,
  sorting,
  expandedId,
  onToggle,
  canManage,
  currentUserId,
  onChanged,
}: {
  tasks: WorkTaskListItem[];
  message: string | null;
  isAdmin: boolean;
  sorting: Sorting;
  expandedId: string | null;
  onToggle: (id: string) => void;
  canManage: boolean;
  currentUserId: string | undefined;
  onChanged: (task: WorkTaskDetailDto) => void;
}) {
  const colSpan = isAdmin ? 7 : 6;
  return (
    <Card className="overflow-hidden">
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
          <tr>
            {SORTABLE_BEFORE_STATUS.map((col) => (
              <SortableHeader key={col.key} colKey={col.key} label={col.label} {...sorting} />
            ))}
            <th scope="col" className="px-4 py-2">
              Status
            </th>
            {SORTABLE_AFTER_STATUS.filter((col) => !col.adminOnly || isAdmin).map((col) => (
              <SortableHeader key={col.key} colKey={col.key} label={col.label} {...sorting} />
            ))}
          </tr>
        </thead>
        <tbody>
          {tasks.map((t, i) => (
            <Fragment key={t.id}>
              <tr className={clsx('cursor-pointer border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')} onClick={() => onToggle(t.id)}>
                <td className="px-4 py-2 font-medium text-slate-800">
                  {/* The row can be clicked with a mouse; this button is the same thing for the keyboard and for a screen reader. */}
                  <button
                    type="button"
                    aria-expanded={expandedId === t.id}
                    className="text-left font-medium hover:underline"
                    onClick={(e) => {
                      e.stopPropagation();
                      onToggle(t.id);
                    }}
                  >
                    {t.title}
                  </button>
                </td>
                <td className="px-4 py-2">
                  <TaskStatusBadge status={t.status} />
                </td>
                <td className="px-4 py-2">{t.assignedToName}</td>
                <td className="px-4 py-2">{t.assignedByName}</td>
                {isAdmin && <td className="px-4 py-2">{t.departmentName}</td>}
                <td className="px-4 py-2">{when(t.startedAtUtc)}</td>
                <td className="px-4 py-2">{when(t.endedAtUtc)}</td>
              </tr>
              {expandedId === t.id && <TaskRowDetail id={t.id} colSpan={colSpan} canManage={canManage} currentUserId={currentUserId} onChanged={onChanged} />}
            </Fragment>
          ))}
          {tasks.length === 0 && message && (
            <tr>
              <td colSpan={colSpan} className="px-4 py-8 text-center text-slate-500">
                {message}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </Card>
  );
}
