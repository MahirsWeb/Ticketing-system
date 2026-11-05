import { apiClient } from './client';
import { queryClient } from './queryClient';
import { emailKeys } from './emailKeys';

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

  connect: (code: string, redirectUri: string, codeVerifier?: string) =>
    apiClient.post<EmailConnectionStatusDto>('/api/email-integration/connect', { code, redirectUri, codeVerifier }).then((r) => {
      void queryClient.invalidateQueries({ queryKey: emailKeys.status });
      return r.data;
    }),

  /** Once disconnected, the status on screen says so at once (nothing is put into memory if there is none on screen), and is asked again to be sure. */
  disconnect: () =>
    apiClient.post('/api/email-integration/disconnect').then((r) => {
      queryClient.setQueryData(emailKeys.status, (open: EmailConnectionStatusDto | undefined) => (open ? { connected: false, connectedEmail: null } : undefined));
      void queryClient.invalidateQueries({ queryKey: emailKeys.status });
      return r.data;
    }),

  listMessages: (take = 25) =>
    apiClient.get<InboxMessageSummaryDto[]>('/api/email-integration/messages', { params: { take } }).then((r) => r.data),

  /** The server answers with what the mark is now; the message in the inbox that is on screen takes it at once. */
  setMarked: (messageId: string, marked: boolean) =>
    apiClient.post<boolean>(`/api/email-integration/messages/${encodeURIComponent(messageId)}/mark`, { marked }).then((r) => {
      const now = r.data;
      queryClient.setQueriesData<InboxMessageSummaryDto[]>({ queryKey: emailKeys.inbox }, (rows) => rows?.map((m) => (m.messageId === messageId ? { ...m, isMarked: now } : m)));
      return now;
    }),

  getPrefill: (messageId: string) =>
    apiClient.get<EmailPrefillDto>(`/api/email-integration/messages/${encodeURIComponent(messageId)}/prefill`).then((r) => r.data),

  completeTicket: (messageId: string, ticketId: string) =>
    apiClient
      .post(`/api/email-integration/messages/${encodeURIComponent(messageId)}/complete-ticket`, null, { params: { ticketId } })
      .then((r) => r.data),
};
