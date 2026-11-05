import { Card } from '../../components/ui';
import { TopFiveList } from './TopFiveList';
import type { BranchBreakdownEntryDto } from '../../types';

/** For administrators: how many tickets each branch has in the period. */
export function BranchBreakdownCard({ items }: { items: BranchBreakdownEntryDto[] }) {
  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">By branch</h2>
      {items.length === 0 ? (
        <p className="text-sm text-slate-500">No routed tickets in this period yet.</p>
      ) : (
        <TopFiveList
          items={items}
          keyOf={(b) => b.departmentId}
          render={(b) => (
            <div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
              <span className="font-medium text-slate-800">{b.departmentName}</span>
              <span className="flex gap-3 text-xs text-slate-500">
                <span>{b.totalNew} new</span>
                <span className="text-amber-600">{b.totalOpen} open</span>
                <span className="text-green-700">{b.totalClosed} closed</span>
              </span>
            </div>
          )}
        />
      )}
    </Card>
  );
}
