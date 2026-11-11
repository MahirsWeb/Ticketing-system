import { Card } from '../../components/ui';
import { TopFiveList } from './TopFiveList';
import type { BranchBreakdownEntryDto } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';

/** For administrators: how many tickets each branch has in the period. */
export function BranchBreakdownCard({ items }: { items: BranchBreakdownEntryDto[] }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">{t('dash_byBranch')}</h2>
      {items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('dash_noRouted')}</p>
      ) : (
        <TopFiveList
          items={items}
          keyOf={(b) => b.departmentId}
          render={(b) => (
            <div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
              <span className="font-medium text-slate-800">{b.departmentName}</span>
              <span className="flex gap-3 text-xs text-slate-600">
                <span>{t('dash_countNew', { count: b.totalNew })}</span>
                <span className="text-amber-700">{t('dash_countOpen', { count: b.totalOpen })}</span>
                <span className="text-green-700">{t('dash_countClosed', { count: b.totalClosed })}</span>
              </span>
            </div>
          )}
        />
      )}
    </Card>
  );
}
