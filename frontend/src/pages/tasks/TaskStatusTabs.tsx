import { Tabs } from '../../components/Tabs';
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
  return <Tabs label="Task status" tabs={STATUS_TABS.map((t) => ({ key: t.key, label: counts ? `${t.label} (${counts[t.key]})` : t.label }))} active={active} onSelect={onSelect} />;
}
