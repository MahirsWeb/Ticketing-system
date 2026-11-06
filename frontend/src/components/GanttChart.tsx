import { useMemo, useState } from 'react';
import clsx from 'clsx';
import type { GanttEntryDto } from '../types';

function describe(e: GanttEntryDto) {
  const from = e.startedAtUtc ? fmtTime(e.startedAtUtc) : '?';
  const to = e.endedAtUtc ? fmtTime(e.endedAtUtc) : 'ongoing';
  // A block without logged work is the time the ticket has been open, not time somebody spent on it: it says so in words, too.
  return `${e.title}, ${e.status}, ${from} to ${to}${e.isLogged === false ? ', open time, no work logged' : ''}`;
}

/// Diagonal stripes on a lighter colour: "the ticket was open", as opposed to the solid colour of logged work.
const ELAPSED_PATTERN = 'repeating-linear-gradient(135deg, rgba(255,255,255,0.55) 0 4px, rgba(255,255,255,0) 4px 8px)';

function fmtTime(iso: string) {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

const TICKET_COLORS: Record<string, string> = {
  New: '#94a3b8',
  Open: '#f59e0b',
  InProgress: '#2563eb',
  Resolved: '#0d9488',
  Closed: '#16a34a',
};

const TASK_COLORS: Record<string, string> = {
  Pending: '#94a3b8',
  InProgress: '#7c3aed',
  Done: '#16a34a',
};

/// Per-person daily timeline combining ticket work sessions and task work sessions on a shared hour
/// axis, so it's immediately visible when a task was squeezed in alongside an open ticket.
export function GanttChart({ entries, date }: { entries: GanttEntryDto[]; date: Date }) {
  const [hovered, setHovered] = useState<GanttEntryDto | null>(null);

  const { dayStart, dayEnd, now, isToday } = useMemo(() => {
    const start = new Date(date);
    start.setHours(0, 0, 0, 0);
    const end = new Date(start);
    end.setDate(end.getDate() + 1);
    const n = new Date();
    return { dayStart: start, dayEnd: end, now: n, isToday: n.toDateString() === start.toDateString() };
  }, [date]);

  const { rangeStart, rangeEnd } = useMemo(() => {
    let min = new Date(dayStart);
    min.setHours(7, 0, 0, 0);
    let max = new Date(dayStart);
    max.setHours(18, 0, 0, 0);

    for (const e of entries) {
      if (e.startedAtUtc) {
        const s = new Date(e.startedAtUtc);
        const padded = new Date(s.getTime() - 30 * 60000);
        if (padded < min) min = padded;
      }
      const endSource = e.endedAtUtc ? new Date(e.endedAtUtc) : isToday ? now : e.startedAtUtc ? new Date(e.startedAtUtc) : null;
      if (endSource) {
        const padded = new Date(endSource.getTime() + 30 * 60000);
        if (padded > max) max = padded;
      }
    }
    if (min < dayStart) min = new Date(dayStart);
    if (max > dayEnd) max = new Date(dayEnd);
    if (max <= min) max = new Date(min.getTime() + 60 * 60000);
    return { rangeStart: min, rangeEnd: max };
  }, [entries, dayStart, dayEnd, isToday, now]);

  const totalMs = rangeEnd.getTime() - rangeStart.getTime() || 1;
  const pct = (d: Date) => Math.min(100, Math.max(0, ((d.getTime() - rangeStart.getTime()) / totalMs) * 100));

  const hourMarks = useMemo(() => {
    const marks: Date[] = [];
    const cursor = new Date(rangeStart);
    cursor.setMinutes(0, 0, 0);
    if (cursor < rangeStart) cursor.setHours(cursor.getHours() + 1);
    while (cursor <= rangeEnd) {
      marks.push(new Date(cursor));
      cursor.setHours(cursor.getHours() + 1);
    }
    return marks;
  }, [rangeStart, rangeEnd]);

  const tickets = entries.filter((e) => e.type === 'Ticket');
  const tasks = entries.filter((e) => e.type === 'WorkTask');
  const nowPct = isToday && now >= rangeStart && now <= rangeEnd ? pct(now) : null;

  const ROW_HEIGHT = 28; // 24px block + 4px gap

  function Lane({ label, items, colors, emptyLabel }: { label: string; items: GanttEntryDto[]; colors: Record<string, string>; emptyLabel: string }) {
    // Greedily pack time-overlapping items into separate sub-rows (earliest start first) so
    // simultaneous work is stacked, never rendered on top of itself.
    const withTimes = items
      .map((e) => {
        const start = e.startedAtUtc ? new Date(e.startedAtUtc) : rangeStart;
        const end = e.endedAtUtc ? new Date(e.endedAtUtc) : isToday ? now : rangeEnd;
        return { entry: e, start, end };
      })
      .sort((a, b) => a.start.getTime() - b.start.getTime());

    const rowEnds: number[] = [];
    const placed = withTimes.map(({ entry, start, end }) => {
      let row = rowEnds.findIndex((endTime) => endTime <= start.getTime());
      if (row === -1) {
        row = rowEnds.length;
        rowEnds.push(end.getTime());
      } else {
        rowEnds[row] = end.getTime();
      }
      return { entry, start, end, row };
    });
    const rowCount = Math.max(1, rowEnds.length);
    const laneHeight = rowCount * ROW_HEIGHT + 4;

    return (
      <div role="group" aria-label={label} className="relative rounded-md bg-slate-50" style={{ height: laneHeight }}>
        {hourMarks.map((h) => (
          <div key={h.toISOString()} className="absolute top-0 h-full w-px bg-slate-200" style={{ left: `${pct(h)}%` }} />
        ))}
        {nowPct !== null && <div className="absolute top-0 z-10 h-full w-0.5 bg-red-500" style={{ left: `${nowPct}%` }} />}
        {items.length === 0 && (
          <div className="flex h-full items-center px-3 text-xs text-slate-600">{emptyLabel}</div>
        )}
        {placed.map(({ entry: e, start, end, row }) => {
          const left = pct(start);
          const width = Math.max(0.6, pct(end) - left);
          const ongoing = !e.endedAtUtc;
          return (
            <button
              type="button"
              key={e.id}
              aria-label={describe(e)}
              title={describe(e)}
              onMouseEnter={() => setHovered(e)}
              onMouseLeave={() => setHovered(null)}
              onFocus={() => setHovered(e)}
              onBlur={() => setHovered(null)}
              className={clsx(
                'absolute flex h-6 items-center overflow-hidden rounded-md px-2 shadow-sm transition-transform hover:z-20 hover:scale-[1.03] focus:z-20 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-1',
                ongoing && 'ring-2 ring-white ring-offset-1 ring-offset-red-400'
              )}
              style={{
                left: `${left}%`,
                width: `${width}%`,
                top: 2 + row * ROW_HEIGHT,
                background: colors[e.status] ?? '#64748b',
                ...(e.isLogged === false ? { backgroundImage: ELAPSED_PATTERN, border: '1px dashed rgba(15,23,42,0.5)' } : {})
              }}
            >
              <span className="truncate text-[10px] font-semibold text-white">{e.title}</span>
            </button>
          );
        })}
      </div>
    );
  }

  return (
    <div>
      <div className="flex items-center gap-3">
        <div className="w-20 shrink-0" />
        <div className="relative h-5 flex-1 border-b border-slate-200 text-[10px] text-slate-600">
          {hourMarks.map((h) => (
            <span key={h.toISOString()} className="absolute -translate-x-1/2" style={{ left: `${pct(h)}%` }}>
              {String(h.getHours()).padStart(2, '0')}:00
            </span>
          ))}
        </div>
      </div>

      <div className="mt-2 flex items-center gap-3">
        <div className="w-20 shrink-0 text-xs font-semibold text-slate-600"><span aria-hidden>🎫</span> Tickets</div>
        <div className="flex-1">
          <Lane label="Ticket work" items={tickets} colors={TICKET_COLORS} emptyLabel="No ticket work logged this day" />
        </div>
      </div>
      <div className="mt-2 flex items-center gap-3">
        <div className="w-20 shrink-0 text-xs font-semibold text-slate-600"><span aria-hidden>📋</span> Tasks</div>
        <div className="flex-1">
          <Lane label="Task work" items={tasks} colors={TASK_COLORS} emptyLabel="No tasks logged this day" />
        </div>
      </div>

      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-slate-600" aria-label="Colour legend">
        {Object.entries(TICKET_COLORS).map(([status, color]) => (
          <span key={'t' + status} className="inline-flex items-center gap-1">
            <span aria-hidden className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: color }} />
            Ticket: {status}
          </span>
        ))}
        {Object.entries(TASK_COLORS).map(([status, color]) => (
          <span key={'k' + status} className="inline-flex items-center gap-1">
            <span aria-hidden className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: color }} />
            Task: {status}
          </span>
        ))}
        <span className="inline-flex items-center gap-1">
          <span aria-hidden className="inline-block h-2.5 w-2.5 rounded-sm border border-dashed border-slate-700 bg-slate-300" style={{ backgroundImage: ELAPSED_PATTERN }} />
          Open time, no work logged
        </span>
      </div>

      <div className="mt-3 h-8" aria-live="polite">
        {hovered ? (
          <div className="rounded-md bg-slate-50 px-3 py-1.5 text-xs text-slate-700">
            <span className="font-semibold">{hovered.title}</span>{' '}
            {hovered.startedAtUtc ? fmtTime(hovered.startedAtUtc) : '?'}
            {hovered.endedAtUtc ? `–${fmtTime(hovered.endedAtUtc)}` : ' (ongoing)'} · {hovered.status}
            {hovered.isLogged === false && ' · open time, no work logged'}
          </div>
        ) : (
          <p className="px-3 text-xs text-slate-600">Hover or focus a block for details.</p>
        )}
      </div>
    </div>
  );
}
