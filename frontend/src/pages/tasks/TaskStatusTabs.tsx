import type { WorkTaskStatus } from '../../types';

const STATUS_TABS: { key: WorkTaskStatus; label: string }[] = [
  { key: 'Pending', label: 'Opened (Pending)' },
  { key: 'InProgress', label: 'In Progress' },
  { key: 'Done', label: 'Done' },
];

/** The statuses a list of tasks can be shown for, with how many tasks each has. `counts` is null while there is nothing to count (the tasks are on their way, or could not be had). */
export function TaskStatusTabs({
  active,
  counts,
  onSelect,
}: {
  active: WorkTaskStatus;
  counts: Record<WorkTaskStatus, number> | null;
  onSelect: (status: WorkTaskStatus) => void;
}) {
  return (
    <div className="flex gap-2 border-b border-slate-200" role="tablist" aria-label="Task status">
      {STATUS_TABS.map((t) => (
        <button
          key={t.key}
          type="button"
          role="tab"
          aria-selected={active === t.key}
          onClick={() => onSelect(t.key)}
          className={`px-3 py-2 text-sm font-medium ${active === t.key ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          {t.label}
          {counts && ` (${counts[t.key]})`}
        </button>
      ))}
    </div>
  );
}
