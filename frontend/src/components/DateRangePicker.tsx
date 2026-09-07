import { useEffect, useMemo, useRef, useState } from 'react';
import {
  addMonths,
  addYears,
  eachDayOfInterval,
  endOfMonth,
  endOfWeek,
  format,
  isAfter,
  isBefore,
  isSameDay,
  isSameMonth,
  isWithinInterval,
  startOfMonth,
  startOfWeek,
  startOfYear,
  subDays,
  subMonths,
  subYears,
} from 'date-fns';

function atStartOfDay(d: Date) {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x;
}
function atEndOfDay(d: Date) {
  const x = new Date(d);
  x.setHours(23, 59, 59, 999);
  return x;
}

const PRESETS: { label: string; range: () => [Date, Date] }[] = [
  { label: 'Today', range: () => [new Date(), new Date()] },
  { label: 'Yesterday', range: () => [subDays(new Date(), 1), subDays(new Date(), 1)] },
  { label: 'This week', range: () => [startOfWeek(new Date(), { weekStartsOn: 1 }), new Date()] },
  { label: 'This month', range: () => [startOfMonth(new Date()), new Date()] },
  { label: 'This year', range: () => [startOfYear(new Date()), new Date()] },
];

const WEEKDAY_LABELS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const YEAR_BLOCK_SIZE = 12;

/// Single-popover "from – to" range picker: pick a start day, optionally an end day (defaults to
/// today if you don't), or jump straight to a preset. Replaces the old two-native-input version
/// everywhere it's used so picking a range never requires two separate calendar popups.
export function DateRangePicker({
  from,
  to,
  onChange,
  placeholder = 'All time',
  clearable = false,
}: {
  from: Date | null;
  to: Date | null;
  onChange: (from: Date | null, to: Date | null) => void;
  placeholder?: string;
  clearable?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [view, setView] = useState<'days' | 'months' | 'years'>('days');
  const [viewMonth, setViewMonth] = useState(() => startOfMonth(from ?? new Date()));
  const [pendingStart, setPendingStart] = useState<Date | null>(from);
  const [pendingEnd, setPendingEnd] = useState<Date | null>(to);
  const [hoverDay, setHoverDay] = useState<Date | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    setPendingStart(from);
    setPendingEnd(to);
    setViewMonth(startOfMonth(from ?? new Date()));
    setView('days');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  function closeAndCommitPending() {
    if (pendingStart && !pendingEnd) onChange(atStartOfDay(pendingStart), atEndOfDay(new Date()));
    setOpen(false);
  }

  useEffect(() => {
    if (!open) return;
    function handleClick(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) closeAndCommitPending();
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, pendingStart, pendingEnd]);

  function pickDay(day: Date) {
    if (!pendingStart || pendingEnd) {
      setPendingStart(day);
      setPendingEnd(null);
      return;
    }
    if (isBefore(day, pendingStart)) {
      setPendingStart(day);
      setPendingEnd(null);
      return;
    }
    setPendingEnd(day);
    onChange(atStartOfDay(pendingStart), atEndOfDay(day));
    setOpen(false);
  }

  function applyPreset(range: [Date, Date]) {
    onChange(atStartOfDay(range[0]), atEndOfDay(range[1]));
    setOpen(false);
  }

  function clear() {
    onChange(null, null);
    setPendingStart(null);
    setPendingEnd(null);
    setOpen(false);
  }

  const days = useMemo(() => {
    const start = startOfWeek(startOfMonth(viewMonth), { weekStartsOn: 1 });
    const end = endOfWeek(endOfMonth(viewMonth), { weekStartsOn: 1 });
    return eachDayOfInterval({ start, end });
  }, [viewMonth]);

  const yearBlockStart = viewMonth.getFullYear() - (viewMonth.getFullYear() % YEAR_BLOCK_SIZE);

  const rangeStart = pendingStart;
  const rangeEnd = pendingEnd ?? (pendingStart && hoverDay && isAfter(hoverDay, pendingStart) ? hoverDay : null);

  const label = !from && !to
    ? placeholder
    : from && to && isSameDay(from, to)
      ? format(from, 'dd.MM.yyyy')
      : `${from ? format(from, 'dd.MM.yyyy') : '…'} – ${to ? format(to, 'dd.MM.yyyy') : 'Today'}`;

  return (
    <div className="relative" ref={rootRef}>
      <button
        type="button"
        onClick={() => (open ? closeAndCommitPending() : setOpen(true))}
        className="flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 shadow-sm hover:border-slate-300"
      >
        <svg className="h-4 w-4 shrink-0 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <rect x="3" y="4" width="18" height="18" rx="2" />
          <path d="M16 2v4M8 2v4M3 10h18" />
        </svg>
        {label}
        {clearable && (from || to) && (
          <span
            role="button"
            onClick={(e) => {
              e.stopPropagation();
              clear();
            }}
            className="ml-1 rounded-full px-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
          >
            ×
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 z-40 mt-2 flex overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg">
          <div className="w-36 shrink-0 border-r border-slate-100 p-2">
            {PRESETS.map((p) => (
              <button
                key={p.label}
                type="button"
                onClick={() => applyPreset(p.range())}
                className="block w-full rounded-md px-2 py-1.5 text-left text-xs font-medium text-slate-600 hover:bg-slate-50 hover:text-blue-700"
              >
                {p.label}
              </button>
            ))}
            {clearable && (
              <button
                type="button"
                onClick={clear}
                className="mt-1 block w-full rounded-md px-2 py-1.5 text-left text-xs font-medium text-slate-400 hover:bg-slate-50 hover:text-red-600"
              >
                All time
              </button>
            )}
          </div>

          <div className="w-64 shrink-0 p-3">
            {view === 'days' && (
              <>
                <div className="mb-2 flex items-center justify-between">
                  <button
                    type="button"
                    onClick={() => setViewMonth((m) => subMonths(m, 1))}
                    className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  >
                    ‹
                  </button>
                  <button
                    type="button"
                    onClick={() => setView('months')}
                    className="rounded px-2 py-0.5 text-sm font-semibold text-slate-700 hover:bg-slate-100"
                  >
                    {format(viewMonth, 'MMMM yyyy')}
                  </button>
                  <button
                    type="button"
                    onClick={() => setViewMonth((m) => addMonths(m, 1))}
                    className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  >
                    ›
                  </button>
                </div>
                <div className="grid grid-cols-7 gap-0.5 text-center text-[11px] font-semibold text-slate-400">
                  {WEEKDAY_LABELS.map((d) => (
                    <div key={d} className="py-1">
                      {d}
                    </div>
                  ))}
                </div>
                <div className="grid grid-cols-7 gap-0.5">
                  {days.map((day) => {
                    const inMonth = isSameMonth(day, viewMonth);
                    const isStart = rangeStart && isSameDay(day, rangeStart);
                    const isEnd = rangeEnd && isSameDay(day, rangeEnd);
                    const inRange = rangeStart && rangeEnd && isWithinInterval(day, { start: rangeStart, end: rangeEnd });
                    const isFuture = isAfter(atStartOfDay(day), atStartOfDay(new Date()));
                    return (
                      <button
                        key={day.toISOString()}
                        type="button"
                        disabled={isFuture}
                        onMouseEnter={() => setHoverDay(day)}
                        onClick={() => pickDay(day)}
                        className={[
                          'h-7 w-7 rounded-md text-xs',
                          !inMonth && 'text-slate-300',
                          inMonth && !isStart && !isEnd && !inRange && 'text-slate-700 hover:bg-slate-100',
                          inRange && !isStart && !isEnd && 'bg-blue-50 text-blue-700',
                          (isStart || isEnd) && 'bg-blue-700 font-semibold text-white hover:bg-blue-800',
                          isFuture && 'cursor-not-allowed text-slate-200 hover:bg-transparent',
                        ]
                          .filter(Boolean)
                          .join(' ')}
                      >
                        {format(day, 'd')}
                      </button>
                    );
                  })}
                </div>
              </>
            )}

            {view === 'months' && (
              <>
                <div className="mb-2 flex items-center justify-between">
                  <button
                    type="button"
                    onClick={() => setViewMonth((m) => subYears(m, 1))}
                    className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  >
                    ‹
                  </button>
                  <button
                    type="button"
                    onClick={() => setView('years')}
                    className="rounded px-2 py-0.5 text-sm font-semibold text-slate-700 hover:bg-slate-100"
                  >
                    {format(viewMonth, 'yyyy')}
                  </button>
                  <button
                    type="button"
                    onClick={() => setViewMonth((m) => addYears(m, 1))}
                    className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  >
                    ›
                  </button>
                </div>
                <div className="grid grid-cols-3 gap-1">
                  {MONTH_LABELS.map((label, idx) => (
                    <button
                      key={label}
                      type="button"
                      onClick={() => {
                        setViewMonth(new Date(viewMonth.getFullYear(), idx, 1));
                        setView('days');
                      }}
                      className={[
                        'rounded-md py-2 text-xs',
                        idx === viewMonth.getMonth() ? 'bg-blue-700 font-semibold text-white hover:bg-blue-800' : 'text-slate-700 hover:bg-slate-100',
                      ].join(' ')}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              </>
            )}

            {view === 'years' && (
              <>
                <div className="mb-2 flex items-center justify-between">
                  <button
                    type="button"
                    onClick={() => setViewMonth((m) => subYears(m, YEAR_BLOCK_SIZE))}
                    className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  >
                    ‹
                  </button>
                  <div className="text-sm font-semibold text-slate-700">
                    {yearBlockStart} – {yearBlockStart + YEAR_BLOCK_SIZE - 1}
                  </div>
                  <button
                    type="button"
                    onClick={() => setViewMonth((m) => addYears(m, YEAR_BLOCK_SIZE))}
                    className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  >
                    ›
                  </button>
                </div>
                <div className="grid grid-cols-3 gap-1">
                  {Array.from({ length: YEAR_BLOCK_SIZE }, (_, i) => yearBlockStart + i).map((year) => (
                    <button
                      key={year}
                      type="button"
                      onClick={() => {
                        setViewMonth(new Date(year, viewMonth.getMonth(), 1));
                        setView('months');
                      }}
                      className={[
                        'rounded-md py-2 text-xs',
                        year === viewMonth.getFullYear() ? 'bg-blue-700 font-semibold text-white hover:bg-blue-800' : 'text-slate-700 hover:bg-slate-100',
                      ].join(' ')}
                    >
                      {year}
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
