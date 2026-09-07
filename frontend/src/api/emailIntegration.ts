import { apiClient } from './client';

export interface EmailConnectionStatusDto {
  connected: boolean;
  connectedEmail: string | null;
}

export interface InboxMessageSummaryDto {
  messageId: string;
  subject: string;
  fromEmail: string;
  fromName: string;
  bodyPreview: string;
  receivedAtUtc: string;
  hasAttachments: boolean;
  isMarked: boolean;
  convertedTicketId: string | null;
}

export interface EmailPrefillDto {
  messageId: string;
  clientEmail: string;
  subject: string;
  bodyHtml: string;
  receivedAtUtc: string;
  hasAttachments: boolean;
}

export const emailIntegrationApi = {
  getConfig: () => apiClient.get<{ clientId: string }>('/api/email-integration/config').then((r) => r.data),

  getStatus: () => apiClient.get<EmailConnectionStatusDto>('/api/email-integration/status').then((r) => r.data),

  connect: (code: string, redirectUri: string) =>
    apiClient.post<EmailConnectionStatusDto>('/api/email-integration/connect', { code, redirectUri }).then((r) => r.data),

  disconnect: () => apiClient.post('/api/email-integration/disconnect').then((r) => r.data),

  listMessages: (take = 25) =>
    apiClient.get<InboxMessageSummaryDto[]>('/api/email-integration/messages', { params: { take } }).then((r) => r.data),

  setMarked: (messageId: string, marked: boolean) =>
    apiClient.post(`/api/email-integration/messages/${encodeURIComponent(messageId)}/mark`, { marked }).then((r) => r.data),

  getPrefill: (messageId: string) =>
    apiClient.get<EmailPrefillDto>(`/api/email-integration/messages/${encodeURIComponent(messageId)}/prefill`).then((r) => r.data),

  completeTicket: (messageId: string, ticketId: string) =>
    apiClient
      .post(`/api/email-integration/messages/${encodeURIComponent(messageId)}/complete-ticket`, null, { params: { ticketId } })
      .then((r) => r.data),
};
