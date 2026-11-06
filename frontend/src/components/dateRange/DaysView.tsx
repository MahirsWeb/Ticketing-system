import { useMemo } from 'react';
import { addMonths, format, isAfter, isBefore, isSameDay, isSameMonth, subMonths } from 'date-fns';
import { HEADING_BUTTON, PickerHeader } from './PickerHeader';
import { WEEKDAY_LABELS, isFutureDay, monthGrid } from './rangeModel';

/** The days of a month, with the range that is being chosen marked. A day that has not come yet cannot be chosen. */
export function DaysView({
  month,
  rangeStart,
  rangeEnd,
  onMonth,
  onTitle,
  onPick,
  onHover,
}: {
  month: Date;
  rangeStart: Date | null;
  rangeEnd: Date | null;
  onMonth: (month: Date) => void;
  onTitle: () => void;
  onPick: (day: Date) => void;
  onHover: (day: Date) => void;
}) {
  const days = useMemo(() => monthGrid(month), [month]);

  return (
    <>
      <PickerHeader previous={{ label: 'Previous month', onClick: () => onMonth(subMonths(month, 1)) }} next={{ label: 'Next month', onClick: () => onMonth(addMonths(month, 1)) }}>
        <button type="button" onClick={onTitle} className={HEADING_BUTTON}>
          {format(month, 'MMMM yyyy')}
        </button>
      </PickerHeader>
      <div className="grid grid-cols-7 gap-0.5 text-center text-[11px] font-semibold text-slate-500">
        {WEEKDAY_LABELS.map((d) => (
          <div key={d} className="py-1">
            {d}
          </div>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-0.5">
        {days.map((day) => {
          const inMonth = isSameMonth(day, month);
          const isStart = !!rangeStart && isSameDay(day, rangeStart);
          const isEnd = !!rangeEnd && isSameDay(day, rangeEnd);
          // By the day, and never failing: a range whose end is before its start (an address can say so) simply has no days between.
          const inRange = !!rangeStart && !!rangeEnd && !isBefore(day, rangeStart) && !isAfter(day, rangeEnd);
          const isFuture = isFutureDay(day);
          return (
            <button
              key={day.toISOString()}
              type="button"
              disabled={isFuture}
              aria-label={format(day, 'd MMMM yyyy')}
              aria-pressed={isStart || isEnd}
              onMouseEnter={() => onHover(day)}
              onFocus={() => onHover(day)}
              onClick={() => onPick(day)}
              className={[
                'h-7 w-7 rounded-md text-xs',
                !inMonth && 'text-slate-500',
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
  );
}
