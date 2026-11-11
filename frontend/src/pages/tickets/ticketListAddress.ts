import { format } from 'date-fns';
import { englishTranslate, type Translate } from '../../i18n/translate';

export const PAGE_SIZE = 20;

export type TicketTab = 'all' | 'new' | 'opened' | 'answered' | 'closed';

export const TABS: { key: TicketTab; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'new', label: 'New' },
  { key: 'opened', label: 'Opened' },
  { key: 'answered', label: 'Answered' },
  { key: 'closed', label: 'Closed' },
];

const OPENED = 'Open,InProgress,Resolved';

export function tabToParams(tab: TicketTab): { status?: string; answered?: string } {
  switch (tab) {
    case 'new':
      return { status: 'New' };
    case 'opened':
      return { status: OPENED };
    case 'answered':
      return { status: OPENED, answered: '1' };
    case 'closed':
      return { status: 'Closed' };
    default:
      return {};
  }
}

/**
 * The group the address is for. `null` when it asks for a status that is none of the groups (written by hand): the list is then narrowed by it,
 * and no group is chosen, because "All" would say the list is everything.
 */
export function tabOf(status: string, answered: boolean): TicketTab | null {
  if (answered) return 'answered';
  switch (status) {
    case '':
      return 'all';
    case 'New':
      return 'new';
    case 'Closed':
      return 'closed';
    case OPENED:
      return 'opened';
    default:
      return null;
  }
}

/** A page number from the address: a whole number, at least 1. Anything else (letters, zero, a fraction) is the first page. */
export function parsePage(raw: string | null): number {
  const n = Math.floor(Number(raw));
  return Number.isSafeInteger(n) && n > 1 ? n : 1;
}

/** A date from the address, or null when there is none or it is not a date (an address written by hand): such a date is left out, not shown. */
export function parseDate(iso: string | null): Date | null {
  if (!iso) return null;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Which period the list is for, in words. */
export function rangeLabel(from: Date | null, to: Date | null, t: Translate = englishTranslate): string {
  if (from) return t('range_fromTo', { from: format(from, 'dd.MM.yyyy'), to: to ? format(to, 'dd.MM.yyyy') : t('range_today') });
  if (to) return t('range_upTo', { to: format(to, 'dd.MM.yyyy') });
  return t('range_allTime');
}
