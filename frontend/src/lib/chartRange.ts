import { endOfDay, isValid, parseISO, startOfDay } from 'date-fns';

/**
 * The period picked by dragging across a chart of days: from the start of the first day to the END of the last one, in the day of
 * the person looking at the chart. A label such as "2026-10-16" read as a date is midnight UTC, which is the middle of the night of
 * the 16th here, so the tickets made during the day on the 16th were left out of the figures of the period that ended on the 16th.
 * Returns null when both ends are the same day (that is a click, not a drag) or a label is not a day.
 */
export function zoomRange(left: string, right: string): { from: Date; to: Date } | null {
  const first = parseISO(left);
  const second = parseISO(right);
  if (!isValid(first) || !isValid(second)) return null;

  const [early, late] = first.getTime() <= second.getTime() ? [first, second] : [second, first];
  if (startOfDay(early).getTime() === startOfDay(late).getTime()) return null;
  return { from: startOfDay(early), to: endOfDay(late) };
}
