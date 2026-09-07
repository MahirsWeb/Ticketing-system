import { apiClient } from './client';

export interface NotificationDto {
  id: string;
  type: string;
  message: string;
  ticketId: string | null;
  isRead: boolean;
  createdAt: string;
}

export const notificationsApi = {
  list: (take = 20) => apiClient.get<NotificationDto[]>('/api/notifications', { params: { take } }).then((r) => r.data),
  markRead: (id: string) => apiClient.post(`/api/notifications/${id}/read`).then((r) => r.data),
  markAllRead: () => apiClient.post('/api/notifications/read-all').then((r) => r.data),
};
