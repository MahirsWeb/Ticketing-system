import { eachDayOfInterval, endOfMonth, endOfWeek, format, isAfter, isSameDay, startOfMonth, startOfWeek, startOfYear, subDays } from 'date-fns';

export function atStartOfDay(d: Date) {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x;
}

export function atEndOfDay(d: Date) {
  const x = new Date(d);
  x.setHours(23, 59, 59, 999);
  return x;
}

/** Ready-made ranges. Each is given by its first and last day, asked for when it is chosen so that "today" is the day of the choosing. */
export const PRESETS: { label: string; range: () => [Date, Date] }[] = [
  { label: 'Today', range: () => [new Date(), new Date()] },
  { label: 'Yesterday', range: () => [subDays(new Date(), 1), subDays(new Date(), 1)] },
  { label: 'This week', range: () => [startOfWeek(new Date(), { weekStartsOn: 1 }), new Date()] },
  { label: 'This month', range: () => [startOfMonth(new Date()), new Date()] },
  { label: 'This year', range: () => [startOfYear(new Date()), new Date()] },
];

export const WEEKDAY_LABELS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
export const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
export const YEAR_BLOCK_SIZE = 12;

/** What the button says: the placeholder, one date, or both (a missing beginning is dots, a missing end is today). */
export function rangeLabel(from: Date | null, to: Date | null, placeholder: string) {
  if (!from && !to) return placeholder;
  if (from && to && isSameDay(from, to)) return format(from, 'dd.MM.yyyy');
  return `${from ? format(from, 'dd.MM.yyyy') : '…'} – ${to ? format(to, 'dd.MM.yyyy') : 'Today'}`;
}

/** The weeks that show a month, Monday to Sunday, with the days of the months next to it that fill the first and the last week. */
export function monthGrid(month: Date) {
  return eachDayOfInterval({ start: startOfWeek(startOfMonth(month), { weekStartsOn: 1 }), end: endOfWeek(endOfMonth(month), { weekStartsOn: 1 }) });
}

/** The first year of the block of years that the shown year is in. */
export function yearBlockStart(month: Date) {
  return month.getFullYear() - (month.getFullYear() % YEAR_BLOCK_SIZE);
}

export function isFutureDay(day: Date) {
  return isAfter(atStartOfDay(day), atStartOfDay(new Date()));
}
