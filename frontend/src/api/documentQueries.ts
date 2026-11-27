import { useQuery } from '@tanstack/react-query';
import { documentsApi, type MonthlyParams } from './documents';

/** A document is the state at the moment it is made: it is asked for again each time the page is opened, and forgotten when it is left. */
export function useTicketCard(id: string | undefined) {
  return useQuery({ queryKey: ['documents', 'ticket-card', id], queryFn: () => documentsApi.ticketCard(id!), enabled: !!id, gcTime: 0, staleTime: 0 });
}

/** The report is made only when asked for (the button), not on every change of the form. */
export function useCompanyMonthly(params: MonthlyParams | null) {
  return useQuery({ queryKey: ['documents', 'company-monthly', params], queryFn: () => documentsApi.monthly(params!), enabled: !!params, gcTime: 0, staleTime: 0 });
}
