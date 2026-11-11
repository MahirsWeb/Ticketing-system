import { eachDayOfInterval, endOfMonth, endOfWeek, format, isAfter, isSameDay, startOfMonth, startOfWeek, startOfYear, subDays } from 'date-fns';
import type { TranslationKey } from '../../i18n/translations';

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
export const PRESETS: { labelKey: TranslationKey; range: () => [Date, Date] }[] = [
  { labelKey: 'common_today', range: () => [new Date(), new Date()] },
  { labelKey: 'common_yesterday', range: () => [subDays(new Date(), 1), subDays(new Date(), 1)] },
  { labelKey: 'range_thisWeek', range: () => [startOfWeek(new Date(), { weekStartsOn: 1 }), new Date()] },
  { labelKey: 'range_thisMonth', range: () => [startOfMonth(new Date()), new Date()] },
  { labelKey: 'range_thisYear', range: () => [startOfYear(new Date()), new Date()] },
];

export const WEEKDAY_KEYS: TranslationKey[] = ['weekday_mo', 'weekday_tu', 'weekday_we', 'weekday_th', 'weekday_fr', 'weekday_sa', 'weekday_su'];
export const MONTH_KEYS: TranslationKey[] = [
  'month_jan', 'month_feb', 'month_mar', 'month_apr', 'month_may', 'month_jun',
  'month_jul', 'month_aug', 'month_sep', 'month_oct', 'month_nov', 'month_dec',
];
export const YEAR_BLOCK_SIZE = 12;

/** What the button says: the placeholder, one date, or both (a missing beginning is dots, a missing end is today). */
export function rangeLabel(from: Date | null, to: Date | null, placeholder: string, today = 'Today') {
  if (!from && !to) return placeholder;
  if (from && to && isSameDay(from, to)) return format(from, 'dd.MM.yyyy');
  return `${from ? format(from, 'dd.MM.yyyy') : '…'} – ${to ? format(to, 'dd.MM.yyyy') : today}`;
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
