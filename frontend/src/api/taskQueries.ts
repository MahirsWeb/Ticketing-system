import { useQuery } from '@tanstack/react-query';
import { workTasksApi, type WorkTaskListParams } from './workTasks';
import { taskKeys } from './taskKeys';

export { taskKeys };

/**
 * The tasks a person is shown. What a list says (who has a task, whether it is done) is what people act on, so a list is never shown from
 * memory: one that is left (gcTime 0) is forgotten, and each combination of filters is a list of its own, so the answer to an earlier choice
 * can never be shown as the answer to the current one. The lists on screen are asked again whenever a task is changed (see `changed` in
 * workTasks.ts).
 */
export function useTaskList(params: WorkTaskListParams) {
  return useQuery({ queryKey: taskKeys.list(params), queryFn: () => workTasksApi.list(params), gcTime: 0 });
}

/**
 * One task, with its description, work sessions and history: asked for when its row is opened and forgotten when it is closed. Another task
 * is another entry, so a slow answer for the one that was opened before can never be shown under the one that is open now. What is done to
 * the task on the page comes back as the whole new task and is written into this entry, not fetched again.
 */
export function useTask(id: string | null) {
  return useQuery({ queryKey: taskKeys.detail(id ?? ''), queryFn: () => workTasksApi.getById(id!), enabled: !!id, gcTime: 0 });
}
