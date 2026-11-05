import { useQuery } from '@tanstack/react-query';
import { ticketsApi, type TicketListParams } from './tickets';
import { ticketKeys } from './ticketKeys';
import { usersApi } from './users';

export { ticketKeys };

/**
 * A list of tickets is never shown from memory: what the person sees says whether a ticket is open or closed, so a list that is left
 * (gcTime 0) is forgotten, and coming back to it shows the loading state and then the current list, as it always did. What the shared layer adds
 * is that a request for the same list asked twice is one request, a failure is reported by the same rules as everywhere else, and the lists
 * on screen are asked again whenever a ticket is changed (see `refreshed` in tickets.ts).
 */
export function useTicketList(params: TicketListParams) {
  return useQuery({ queryKey: ticketKeys.list(params), queryFn: () => ticketsApi.list(params), gcTime: 0 });
}

/** The numbers in the menu. The menu is always on screen, so these are asked again whenever a ticket is changed. */
export function useTicketCounts() {
  return useQuery({ queryKey: ticketKeys.counts, queryFn: ticketsApi.counts });
}

/**
 * One ticket. Forgotten as soon as it is left, so another ticket never shows what the previous one had. Changes made on the page come back
 * as the whole new ticket and are written into this entry (`setQueryData`), not fetched again.
 */
export function useTicket(id: string | undefined) {
  return useQuery({ queryKey: ticketKeys.detail(id ?? ''), queryFn: () => ticketsApi.getById(id!), enabled: !!id, gcTime: 0 });
}

/** The staff, for the "assigned to" filter. Asked again on each visit; the list is short and people are added now and then. */
export function useEmployees(options: { enabled?: boolean } = {}) {
  return useQuery({ queryKey: ['users', 'employees'], queryFn: () => usersApi.list({ role: 'Employee' }), enabled: options.enabled ?? true });
}
