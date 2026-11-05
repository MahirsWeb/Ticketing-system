import { apiClient } from './client';
import { queryClient } from './queryClient';
import { ticketKeys } from './ticketKeys';
import type {
  MessageType,
  PagedResult,
  TicketAttachmentDto,
  TicketCountsDto,
  TicketDetailDto,
  TicketListItem,
  TicketPriority,
  TicketSource,
} from '../types';

export interface TicketListParams {
  status?: string;
  companyId?: string;
  clientId?: string;
  assignedToUserId?: string;
  departmentId?: string;
  subBranchId?: string;
  search?: string;
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
  createdFrom?: string;
  createdTo?: string;
  answeredOnly?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * Whatever is done to a ticket can change what the lists show (its status, who has it, whether it has been answered) and the numbers in the
 * menu, so the ones on screen are asked again. The ticket's own page is not: the server answers every change with the whole ticket, and the page takes it from there.
 */
function refreshed<T>(request: Promise<T>): Promise<T> {
  return request.then((result) => {
    void queryClient.invalidateQueries({ queryKey: ticketKeys.lists });
    void queryClient.invalidateQueries({ queryKey: ticketKeys.counts });
    return result;
  });
}

function idempotencyHeaders(key?: string) {
  return key ? { headers: { 'Idempotency-Key': key } } : undefined;
}

export const ticketsApi = {
  create: (title: string, description: string, departmentId: string, idempotencyKey?: string) =>
    refreshed(
      apiClient
        .post<TicketDetailDto>('/api/tickets', { title, description, departmentId }, idempotencyHeaders(idempotencyKey))
        .then((r) => r.data)
    ),

  createOnBehalf: (
    payload: {
      clientEmail: string;
      title: string;
      description: string;
      source: TicketSource;
      helpTopicId: string;
      departmentId: string;
      subBranchId?: string;
      slaPlanId: string;
      priority: TicketPriority;
      dueDateUtc: string | null;
      assignedToUserId: string;
      categoryId?: string;
    },
    idempotencyKey?: string
  ) => refreshed(apiClient.post<TicketDetailDto>('/api/tickets/on-behalf', payload, idempotencyHeaders(idempotencyKey)).then((r) => r.data)),

  list: (params: TicketListParams) =>
    apiClient.get<PagedResult<TicketListItem>>('/api/tickets', { params }).then((r) => r.data),

  counts: () => apiClient.get<TicketCountsDto>('/api/tickets/counts').then((r) => r.data),

  getById: (id: string) => apiClient.get<TicketDetailDto>(`/api/tickets/${id}`).then((r) => r.data),

  open: (
    id: string,
    payload: {
      source: TicketSource;
      helpTopicId: string;
      departmentId: string;
      subBranchId?: string;
      slaPlanId: string;
      priority: TicketPriority;
      dueDateUtc: string | null;
      assignedToUserId: string;
      categoryId?: string;
    }
  ) => refreshed(apiClient.post<TicketDetailDto>(`/api/tickets/${id}/open`, payload).then((r) => r.data)),

  close: (id: string, resolutionSummary: string) =>
    refreshed(apiClient.post<TicketDetailDto>(`/api/tickets/${id}/close`, { resolutionSummary }).then((r) => r.data)),

  reopen: (id: string) => refreshed(apiClient.post<TicketDetailDto>(`/api/tickets/${id}/reopen`).then((r) => r.data)),

  setStatus: (id: string, status: 'Open' | 'InProgress' | 'Resolved') =>
    refreshed(apiClient.post<TicketDetailDto>(`/api/tickets/${id}/status`, { status }).then((r) => r.data)),

  transferBranch: (id: string, departmentId: string, subBranchId?: string) =>
    refreshed(apiClient.post<TicketDetailDto>(`/api/tickets/${id}/transfer-branch`, { departmentId, subBranchId: subBranchId || undefined }).then((r) => r.data)),

  addAssignee: (id: string, userId: string) =>
    refreshed(apiClient.post<TicketDetailDto>(`/api/tickets/${id}/assignees`, { userId }).then((r) => r.data)),

  removeAssignee: (id: string, userId: string) =>
    refreshed(apiClient.delete<TicketDetailDto>(`/api/tickets/${id}/assignees/${userId}`).then((r) => r.data)),

  addWorkSession: (id: string, startedAtUtc: string, endedAtUtc: string | null) =>
    refreshed(apiClient.post<TicketDetailDto>(`/api/tickets/${id}/work-sessions`, { startedAtUtc, endedAtUtc }).then((r) => r.data)),

  updateWorkSession: (id: string, sessionId: string, startedAtUtc: string, endedAtUtc: string | null) =>
    refreshed(apiClient.put<TicketDetailDto>(`/api/tickets/${id}/work-sessions/${sessionId}`, { startedAtUtc, endedAtUtc }).then((r) => r.data)),

  deleteWorkSession: (id: string, sessionId: string) =>
    refreshed(apiClient.delete<TicketDetailDto>(`/api/tickets/${id}/work-sessions/${sessionId}`).then((r) => r.data)),

  addMessage: (id: string, type: MessageType, bodyHtml: string, idempotencyKey?: string) =>
    refreshed(apiClient.post(`/api/tickets/${id}/messages`, { type, bodyHtml }, idempotencyHeaders(idempotencyKey)).then((r) => r.data)),

  uploadAttachments: (id: string, files: File[], messageId?: string) => {
    const form = new FormData();
    files.forEach((f) => form.append('files', f));
    if (messageId) form.append('messageId', messageId);
    return apiClient
      .post<TicketAttachmentDto[]>(`/api/tickets/${id}/attachments`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data);
  },
};
