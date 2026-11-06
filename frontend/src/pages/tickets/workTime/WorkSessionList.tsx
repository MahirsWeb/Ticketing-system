import { format } from 'date-fns';
import type { TicketWorkSessionDto } from '../../../types';
import { Button } from '../../../components/ui';
import { durationOf, formatDuration } from './workTimeModel';

const when = (iso: string) => format(new Date(iso), 'dd.MM.yyyy HH:mm');

/** The sessions of the ticket, each with what can be done to it. Every button says which session it is for, so that the same word in a list of them can be told apart. */
export function WorkSessionList({
  sessions,
  now,
  saving,
  onEnd,
  onEdit,
  onDelete,
}: {
  sessions: TicketWorkSessionDto[];
  now: number;
  saving: boolean;
  onEnd: (s: TicketWorkSessionDto) => void;
  onEdit: (s: TicketWorkSessionDto) => void;
  onDelete: (s: TicketWorkSessionDto) => void;
}) {
  if (sessions.length === 0) return <p className="mb-3 text-sm text-slate-600">No work logged yet.</p>;

  return (
    <ul className="mb-3 divide-y divide-slate-100 rounded-md border border-slate-100 bg-white text-sm">
      {sessions.map((s) => {
        const whose = `the session of ${s.userName} from ${when(s.startedAtUtc)}`;
        return (
          <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
            <div>
              <span className="font-medium text-slate-700">{s.userName}</span>
              <span className="ml-2 tabular-nums text-slate-600">
                {when(s.startedAtUtc)} –{' '}
                {s.endedAtUtc ? when(s.endedAtUtc) : <span className="font-semibold text-green-700">running</span>}
              </span>
              <span className="ml-2 text-xs text-slate-600">{formatDuration(durationOf(s, now))}</span>
            </div>
            <div className="flex items-center gap-1">
              {!s.endedAtUtc && (
                <Button type="button" variant="secondary" className="px-2 py-1 text-xs" disabled={saving} aria-label={`End now: ${whose}`} onClick={() => onEnd(s)}>
                  End now
                </Button>
              )}
              <Button type="button" variant="ghost" className="px-2 py-1 text-xs" disabled={saving} aria-label={`Edit ${whose}`} onClick={() => onEdit(s)}>
                Edit
              </Button>
              <Button type="button" variant="ghost" className="px-2 py-1 text-xs text-red-600" disabled={saving} aria-label={`Delete ${whose}`} onClick={() => onDelete(s)}>
                Delete
              </Button>
            </div>
          </li>
        );
      })}
    </ul>
  );
}
