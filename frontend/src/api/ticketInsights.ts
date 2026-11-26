import { apiClient } from './client';
import type { AttentionDto, FirstResponseDto, ResolutionTimeDto, TransfersDto } from '../types';

/** The period, and for Admin the branch the figures are narrowed to (for staff the server uses their own branch whatever is sent). */
export interface TicketInsightParams {
  from?: string;
  to?: string;
  departmentId?: string;
}

export type InactiveDays = 2 | 3 | 7;

const base = '/api/reports/tickets';

export const ticketInsightsApi = {
  attention: (params: { departmentId?: string; inactiveDays: InactiveDays }) => apiClient.get<AttentionDto>(`${base}/attention`, { params }).then((r) => r.data),
  resolution: (params: TicketInsightParams) => apiClient.get<ResolutionTimeDto>(`${base}/resolution`, { params }).then((r) => r.data),
  firstResponse: (params: TicketInsightParams) => apiClient.get<FirstResponseDto>(`${base}/first-response`, { params }).then((r) => r.data),
  transfers: (params: TicketInsightParams) => apiClient.get<TransfersDto>(`${base}/transfers`, { params }).then((r) => r.data),
};
