import { addYears, format, subYears } from 'date-fns';
import { HEADING_BUTTON, PickerHeader } from './PickerHeader';
import { MONTH_LABELS, YEAR_BLOCK_SIZE, yearBlockStart } from './rangeModel';

const CELL = 'rounded-md py-2 text-xs';
const CHOSEN = 'bg-blue-700 font-semibold text-white hover:bg-blue-800';
const OTHER = 'text-slate-700 hover:bg-slate-100';

/** The twelve months of a year: choosing one goes back to its days. */
export function MonthsView({ month, onYear, onTitle, onPick }: { month: Date; onYear: (month: Date) => void; onTitle: () => void; onPick: (month: Date) => void }) {
  return (
    <>
      <PickerHeader previous={{ label: 'Previous year', onClick: () => onYear(subYears(month, 1)) }} next={{ label: 'Next year', onClick: () => onYear(addYears(month, 1)) }}>
        <button type="button" onClick={onTitle} className={HEADING_BUTTON}>
          {format(month, 'yyyy')}
        </button>
      </PickerHeader>
      <div className="grid grid-cols-3 gap-1">
        {MONTH_LABELS.map((label, index) => (
          <button key={label} type="button" onClick={() => onPick(new Date(month.getFullYear(), index, 1))} className={[CELL, index === month.getMonth() ? CHOSEN : OTHER].join(' ')}>
            {label}
          </button>
        ))}
      </div>
    </>
  );
}

/** A block of twelve years: choosing one goes to its months. */
export function YearsView({ month, onBlock, onPick }: { month: Date; onBlock: (month: Date) => void; onPick: (month: Date) => void }) {
  const first = yearBlockStart(month);
  return (
    <>
      <PickerHeader previous={{ label: 'Previous years', onClick: () => onBlock(subYears(month, YEAR_BLOCK_SIZE)) }} next={{ label: 'Next years', onClick: () => onBlock(addYears(month, YEAR_BLOCK_SIZE)) }}>
        <div className="text-sm font-semibold text-slate-700">
          {first} – {first + YEAR_BLOCK_SIZE - 1}
        </div>
      </PickerHeader>
      <div className="grid grid-cols-3 gap-1">
        {Array.from({ length: YEAR_BLOCK_SIZE }, (_, i) => first + i).map((year) => (
          <button key={year} type="button" onClick={() => onPick(new Date(year, month.getMonth(), 1))} className={[CELL, year === month.getFullYear() ? CHOSEN : OTHER].join(' ')}>
            {year}
          </button>
        ))}
      </div>
    </>
  );
}
