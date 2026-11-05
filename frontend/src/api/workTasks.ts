import { apiClient } from './client';
import { queryClient } from './queryClient';
import { taskKeys } from './taskKeys';
import type { BranchGanttResponse, GanttResponse, WorkTaskDetailDto, WorkTaskListItem, WorkTaskStatus } from '../types';

export interface WorkTaskListParams {
  departmentId?: string;
  assignedToUserId?: string;
  status?: WorkTaskStatus;
}

const listItemOf = (task: WorkTaskDetailDto): WorkTaskListItem => {
  const { description: _description, history: _history, sessions: _sessions, ...item } = task;
  return item;
};

/**
 * Whatever is done to a task comes back as the whole task, and it can change what the lists show (who has it, its status, when work on it
 * started or ended). The task that is open on the page is replaced by it, the rows of the lists on screen take what they show from it at once
 * (so a row does not vanish and come back while the lists are asked again), and the lists are asked again to be sure.
 */
function changed(request: Promise<WorkTaskDetailDto>): Promise<WorkTaskDetailDto> {
  return request.then((task) => {
    // Only an entry that is on screen is replaced: nothing that nobody shows is put into the cache.
    queryClient.setQueryData(taskKeys.detail(task.id), (open: WorkTaskDetailDto | undefined) => (open ? task : undefined));
    queryClient.setQueriesData<WorkTaskListItem[]>({ queryKey: taskKeys.lists }, (rows) => rows?.map((row) => (row.id === task.id ? listItemOf(task) : row)));
    void queryClient.invalidateQueries({ queryKey: taskKeys.lists });
    return task;
  });
}

export const workTasksApi = {
  create: (title: string, description: string, assignedToUserId: string) =>
    apiClient
      .post<WorkTaskDetailDto>('/api/work-tasks', { title, description, assignedToUserId })
      .then((r) => {
        void queryClient.invalidateQueries({ queryKey: taskKeys.lists });
        return r.data;
      }),

  list: (params: WorkTaskListParams = {}) =>
    apiClient.get<WorkTaskListItem[]>('/api/work-tasks', { params }).then((r) => r.data),

  getById: (id: string) => apiClient.get<WorkTaskDetailDto>(`/api/work-tasks/${id}`).then((r) => r.data),

  reassign: (id: string, assignedToUserId: string) =>
    changed(apiClient.post<WorkTaskDetailDto>(`/api/work-tasks/${id}/reassign`, { assignedToUserId }).then((r) => r.data)),

  setTime: (id: string, startedAtUtc: string | null, endedAtUtc: string | null) =>
    changed(apiClient.patch<WorkTaskDetailDto>(`/api/work-tasks/${id}/time`, { startedAtUtc, endedAtUtc }).then((r) => r.data)),

  start: (id: string) => changed(apiClient.post<WorkTaskDetailDto>(`/api/work-tasks/${id}/start`).then((r) => r.data)),

  stop: (id: string) => changed(apiClient.post<WorkTaskDetailDto>(`/api/work-tasks/${id}/stop`).then((r) => r.data)),

  gantt: (userId: string, date?: string) =>
    apiClient.get<GanttResponse>('/api/work-tasks/gantt', { params: { userId, date } }).then((r) => r.data),

  ganttBranch: (departmentId: string, date?: string) =>
    apiClient.get<BranchGanttResponse>('/api/work-tasks/gantt/branch', { params: { departmentId, date } }).then((r) => r.data),
};
