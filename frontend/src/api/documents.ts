import { apiClient } from './client';
import type { CompanyMonthlyAudience, CompanyMonthlyDto, TicketCardDto } from '../types';

export interface MonthlyParams {
  companyId: string;
  year: number;
  month: number;
  audience: CompanyMonthlyAudience;
  includeHours?: boolean;
}

/** Hands the file to the browser as a download, under the name the server gave it. */
async function download(path: string, params: object, fallbackName: string) {
  const response = await apiClient.get(path, { params, responseType: 'blob' });
  const disposition = response.headers['content-disposition'] as string | undefined;
  const filename = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? fallbackName;
  const url = window.URL.createObjectURL(new Blob([response.data]));
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
}

export const documentsApi = {
  ticketCard: (id: string) => apiClient.get<TicketCardDto>(`/api/tickets/${id}/card`).then((r) => r.data),
  ticketCardXlsx: (id: string) => download(`/api/tickets/${id}/card/export`, {}, 'ticket-card.xlsx'),
  monthly: (params: MonthlyParams) => apiClient.get<CompanyMonthlyDto>('/api/reports/company-monthly', { params }).then((r) => r.data),
  monthlyXlsx: (params: MonthlyParams) => download('/api/reports/company-monthly/export', params, 'company-report.xlsx'),
};
