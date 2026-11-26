import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ticketInsightsApi, type InactiveDays, type TicketInsightParams } from './ticketInsights';

/** One entry per selection, forgotten when the selection is left, like the other figures (see reportQueries.ts). */
const FIGURES = { gcTime: 0, placeholderData: keepPreviousData } as const;

export function useAttention(params: { departmentId?: string; inactiveDays: InactiveDays }) {
  return useQuery({ queryKey: ['ticket-insights', 'attention', params], queryFn: () => ticketInsightsApi.attention(params), ...FIGURES });
}

export function useResolutionTime(params: TicketInsightParams) {
  return useQuery({ queryKey: ['ticket-insights', 'resolution', params], queryFn: () => ticketInsightsApi.resolution(params), ...FIGURES });
}

export function useFirstResponse(params: TicketInsightParams) {
  return useQuery({ queryKey: ['ticket-insights', 'first-response', params], queryFn: () => ticketInsightsApi.firstResponse(params), ...FIGURES });
}

export function useTransfers(params: TicketInsightParams) {
  return useQuery({ queryKey: ['ticket-insights', 'transfers', params], queryFn: () => ticketInsightsApi.transfers(params), ...FIGURES });
}
