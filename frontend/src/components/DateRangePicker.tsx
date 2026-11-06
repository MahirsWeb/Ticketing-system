import { DaysView } from './dateRange/DaysView';
import { MonthsView, YearsView } from './dateRange/MonthsAndYearsViews';
import { PRESETS, rangeLabel } from './dateRange/rangeModel';
import { useRangePicker } from './dateRange/useRangePicker';

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
  const picker = useRangePicker({ from, to, onChange });
  const { view, viewMonth } = picker;

  return (
    <div className="relative" ref={picker.rootRef}>
      <div className="flex items-center rounded-xl border border-slate-200 bg-white shadow-sm hover:border-slate-300">
        <button
          ref={picker.triggerRef}
          type="button"
          aria-haspopup="dialog"
          aria-expanded={picker.open}
          onClick={picker.toggle}
          className="flex items-center gap-2 rounded-xl px-3 py-2 text-sm text-slate-700"
        >
          <svg className="h-4 w-4 shrink-0 text-slate-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="3" y="4" width="18" height="18" rx="2" />
            <path d="M16 2v4M8 2v4M3 10h18" />
          </svg>
          {rangeLabel(from, to, placeholder)}
        </button>
        {clearable && (from || to) && (
          <button type="button" aria-label="Clear" onClick={picker.clear} className="mr-2 rounded-full px-1 text-slate-600 hover:bg-slate-100 hover:text-slate-800">
            ×
          </button>
        )}
      </div>

      {picker.open && (
        <div role="dialog" aria-label="Choose a date range" className="absolute right-0 z-40 mt-2 flex overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg">
          <div className="w-36 shrink-0 border-r border-slate-100 p-2">
            {PRESETS.map((p) => (
              <button
                key={p.label}
                type="button"
                onClick={() => picker.applyPreset(p.range())}
                className="block w-full rounded-md px-2 py-1.5 text-left text-xs font-medium text-slate-600 hover:bg-slate-50 hover:text-blue-700"
              >
                {p.label}
              </button>
            ))}
            {clearable && (
              <button
                type="button"
                onClick={picker.clear}
                className="mt-1 block w-full rounded-md px-2 py-1.5 text-left text-xs font-medium text-slate-600 hover:bg-slate-50 hover:text-red-600"
              >
                All time
              </button>
            )}
          </div>

          <div className="w-64 shrink-0 p-3">
            {view === 'days' && (
              <DaysView
                month={viewMonth}
                rangeStart={picker.rangeStart}
                rangeEnd={picker.rangeEnd}
                onMonth={picker.setViewMonth}
                onTitle={() => picker.setView('months')}
                onPick={picker.pickDay}
                onHover={picker.setHoverDay}
              />
            )}
            {view === 'months' && (
              <MonthsView
                month={viewMonth}
                onYear={picker.setViewMonth}
                onTitle={() => picker.setView('years')}
                onPick={(month) => {
                  picker.setViewMonth(month);
                  picker.setView('days');
                }}
              />
            )}
            {view === 'years' && (
              <YearsView
                month={viewMonth}
                onBlock={picker.setViewMonth}
                onPick={(month) => {
                  picker.setViewMonth(month);
                  picker.setView('months');
                }}
              />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
