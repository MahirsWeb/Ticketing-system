import { parseTime } from './workTimeModel';

/// Always-24h time input (native <input type="time"> renders AM/PM per OS locale, so we build our own). What is typed is the value;
/// when the field is left, a time that was understood is written in full ("930" as "09:30"), and one that was not is left as it was
/// typed and marked, so it is never taken for another time.
export function TimeInput({ value, onChange, label }: { value: string; onChange: (v: string) => void; label: string }) {
  const understood = parseTime(value);

  return (
    <input
      type="text"
      aria-label={label}
      aria-invalid={understood === null}
      inputMode="numeric"
      placeholder="00:00"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      onBlur={() => {
        if (understood !== null && understood !== value) onChange(understood);
      }}
      className="w-16 rounded-md border border-slate-300 px-2 py-1.5 text-center text-sm tabular-nums focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600 aria-[invalid=true]:border-red-500 aria-[invalid=true]:ring-1 aria-[invalid=true]:ring-red-500"
    />
  );
}
