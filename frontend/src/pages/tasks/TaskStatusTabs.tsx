import { Tabs } from '../../components/Tabs';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';
import type { WorkTaskStatus } from '../../types';

const STATUS_TABS: { key: WorkTaskStatus; labelKey: TranslationKey }[] = [
  { key: 'Pending', labelKey: 'taskTab_pending' },
  { key: 'InProgress', labelKey: 'taskTab_inProgress' },
  { key: 'Done', labelKey: 'taskTab_done' },
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
  const { t } = useLanguage();
  return <Tabs label={t('task_statusLabel')} tabs={STATUS_TABS.map((x) => ({ key: x.key, label: counts ? `${t(x.labelKey)} (${counts[x.key]})` : t(x.labelKey) }))} active={active} onSelect={onSelect} />;
}
