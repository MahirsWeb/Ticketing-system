import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';
import clsx from 'clsx';

const STATUS_STYLES: Record<string, string> = {
  Pending: 'bg-slate-100 text-slate-600',
  InProgress: 'bg-blue-100 text-blue-800',
  Done: 'bg-green-100 text-green-800',
};

export function TaskStatusBadge({ status }: { status: string }) {
  const { t } = useLanguage();
  const label = STATUS_STYLES[status] ? t(`taskStatus_${status}` as TranslationKey) : status;
  return (
    <span className={clsx('rounded-full px-2.5 py-0.5 text-xs font-semibold', STATUS_STYLES[status] ?? 'bg-slate-100 text-slate-600')}>
      {label}
    </span>
  );
}
