import type { TranslationKey } from '../../i18n/translations';
import type { WorkTaskListItem, WorkTaskStatus } from '../../types';

/** The columns a list of tasks can be sorted by. `adminOnly` ones are only shown to an administrator. */
export const SORTABLE_BEFORE_STATUS: { key: string; labelKey: TranslationKey }[] = [{ key: 'title', labelKey: 'table_title' }];
export const SORTABLE_AFTER_STATUS: { key: string; labelKey: TranslationKey; adminOnly?: boolean }[] = [
  { key: 'assignedTo', labelKey: 'table_assignedTo' },
  { key: 'assignedBy', labelKey: 'taskcol_assignedBy' },
  { key: 'branch', labelKey: 'detail_branch', adminOnly: true },
  { key: 'started', labelKey: 'taskcol_started' },
  { key: 'ended', labelKey: 'taskcol_ended' },
];

const TEXT: Record<string, (t: WorkTaskListItem) => string> = {
  title: (t) => t.title,
  assignedTo: (t) => t.assignedToName,
  assignedBy: (t) => t.assignedByName,
  branch: (t) => t.departmentName,
};
const TIME: Record<string, (t: WorkTaskListItem) => string | null> = {
  started: (t) => t.startedAtUtc,
  ended: (t) => t.endedAtUtc,
};

/** How many tasks each status has. */
export function countByStatus(tasks: { status: WorkTaskStatus }[]): Record<WorkTaskStatus, number> {
  const counts = { Pending: 0, InProgress: 0, Done: 0 };
  for (const t of tasks) counts[t.status]++;
  return counts;
}

// Capital letters do not matter and accented ones are placed as the language places them (Š among the S's), not after Z.
const collator = new Intl.Collator(undefined, { sensitivity: 'accent' });
// What nobody has worked on has no time yet and comes before everything that has one when ascending.
const instant = (iso: string | null) => (iso ? Date.parse(iso) : Number.NEGATIVE_INFINITY);

/** The tasks in the order of the column that is chosen, or as they are when none is. Times are compared as times: "…:00Z" and "…:00.5Z" are not letters. */
export function sortTasks(tasks: WorkTaskListItem[], sortBy: string | null, sortDir: 'asc' | 'desc'): WorkTaskListItem[] {
  const dir = sortDir === 'asc' ? 1 : -1;
  const text = sortBy ? TEXT[sortBy] : undefined;
  if (text) return [...tasks].sort((a, b) => collator.compare(text(a), text(b)) * dir);
  const time = sortBy ? TIME[sortBy] : undefined;
  if (time) return [...tasks].sort((a, b) => Math.sign(instant(time(a)) - instant(time(b))) * dir || 0);
  return tasks;
}
