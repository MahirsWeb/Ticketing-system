import { Card } from '../../components/ui';
import { TopFiveList } from './TopFiveList';
import type { LeaderboardEntryDto } from '../../types';

/** Who closed the most tickets in the period. */
export function LeaderboardCard({ entries }: { entries: LeaderboardEntryDto[] }) {
  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">Top agents (closed tickets)</h2>
      {entries.length === 0 ? (
        <p className="text-sm text-slate-600">No closed tickets in this period yet.</p>
      ) : (
        <TopFiveList
          items={entries}
          keyOf={(entry) => entry.userId}
          render={(entry, i) => (
            <div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
              <span>
                <span className="mr-2 font-bold text-slate-600">#{i + 1}</span>
                {entry.name}
              </span>
              <span className="font-semibold text-slate-800">{entry.closedCount} closed</span>
            </div>
          )}
        />
      )}
    </Card>
  );
}
