import { apiClient } from './client';
import type { OverdueNotificationSettingsDto } from '../types';

export const settingsApi = {
  getOverdueNotifications: () =>
    apiClient.get<OverdueNotificationSettingsDto>('/api/settings/overdue-notifications').then((r) => r.data),
  updateOverdueNotifications: (notifyOnSlaBreach: boolean, manualOverdueDays: number | null) =>
    apiClient
      .put<OverdueNotificationSettingsDto>('/api/settings/overdue-notifications', { notifyOnSlaBreach, manualOverdueDays })
      .then((r) => r.data),
};
