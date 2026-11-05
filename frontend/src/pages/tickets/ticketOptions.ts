import type { TicketPriority, TicketSource } from '../../types';

export const SOURCES: TicketSource[] = ['Phone', 'Email', 'TicketSystem', 'Other'];
export const PRIORITIES: TicketPriority[] = ['Emergency', 'High', 'Medium', 'Low'];

/** The day a person picked as the due date, as the end of that day in their own time zone, in UTC; null when none was picked. */
export function dueDateToUtc(dueDate: string): string | null {
  return dueDate ? new Date(`${dueDate}T23:59:59`).toISOString() : null;
}
