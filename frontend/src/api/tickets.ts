import { apiClient } from './client';
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

export const ticketsApi = {
  create: (title: string, description: string, departmentId: string) =>
    apiClient.post<TicketDetailDto>('/api/tickets', { title, description, departmentId }).then((r) => r.data),

  createOnBehalf: (payload: {
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
  }) => apiClient.post<TicketDetailDto>('/api/tickets/on-behalf', payload).then((r) => r.data),

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
  ) => apiClient.post<TicketDetailDto>(`/api/tickets/${id}/open`, payload).then((r) => r.data),

  close: (id: string, resolutionSummary: string) =>
    apiClient.post<TicketDetailDto>(`/api/tickets/${id}/close`, { resolutionSummary }).then((r) => r.data),

  reopen: (id: string) => apiClient.post<TicketDetailDto>(`/api/tickets/${id}/reopen`).then((r) => r.data),

  transferBranch: (id: string, departmentId: string) =>
    apiClient.post<TicketDetailDto>(`/api/tickets/${id}/transfer-branch`, { departmentId }).then((r) => r.data),

  addAssignee: (id: string, userId: string) =>
    apiClient.post<TicketDetailDto>(`/api/tickets/${id}/assignees`, { userId }).then((r) => r.data),

  removeAssignee: (id: string, userId: string) =>
    apiClient.delete<TicketDetailDto>(`/api/tickets/${id}/assignees/${userId}`).then((r) => r.data),

  setWorkTime: (id: string, startedAtUtc: string | null, endedAtUtc: string | null) =>
    apiClient.patch<TicketDetailDto>(`/api/tickets/${id}/work-time`, { startedAtUtc, endedAtUtc }).then((r) => r.data),

  addMessage: (id: string, type: MessageType, bodyHtml: string) =>
    apiClient.post(`/api/tickets/${id}/messages`, { type, bodyHtml }).then((r) => r.data),

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
