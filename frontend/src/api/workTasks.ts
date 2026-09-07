import { apiClient } from './client';
import type { BranchGanttResponse, GanttResponse, WorkTaskDetailDto, WorkTaskListItem, WorkTaskStatus } from '../types';

export interface WorkTaskListParams {
  departmentId?: string;
  assignedToUserId?: string;
  status?: WorkTaskStatus;
}

export const workTasksApi = {
  create: (title: string, description: string, assignedToUserId: string) =>
    apiClient.post<WorkTaskDetailDto>('/api/work-tasks', { title, description, assignedToUserId }).then((r) => r.data),

  list: (params: WorkTaskListParams = {}) =>
    apiClient.get<WorkTaskListItem[]>('/api/work-tasks', { params }).then((r) => r.data),

  getById: (id: string) => apiClient.get<WorkTaskDetailDto>(`/api/work-tasks/${id}`).then((r) => r.data),

  reassign: (id: string, assignedToUserId: string) =>
    apiClient.post<WorkTaskDetailDto>(`/api/work-tasks/${id}/reassign`, { assignedToUserId }).then((r) => r.data),

  setTime: (id: string, startedAtUtc: string | null, endedAtUtc: string | null) =>
    apiClient.patch<WorkTaskDetailDto>(`/api/work-tasks/${id}/time`, { startedAtUtc, endedAtUtc }).then((r) => r.data),

  start: (id: string) => apiClient.post<WorkTaskDetailDto>(`/api/work-tasks/${id}/start`).then((r) => r.data),

  stop: (id: string) => apiClient.post<WorkTaskDetailDto>(`/api/work-tasks/${id}/stop`).then((r) => r.data),

  gantt: (userId: string, date?: string) =>
    apiClient.get<GanttResponse>('/api/work-tasks/gantt', { params: { userId, date } }).then((r) => r.data),

  ganttBranch: (departmentId: string, date?: string) =>
    apiClient.get<BranchGanttResponse>('/api/work-tasks/gantt/branch', { params: { departmentId, date } }).then((r) => r.data),
};
