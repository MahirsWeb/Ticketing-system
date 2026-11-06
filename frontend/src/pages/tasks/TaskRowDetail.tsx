import { useTask } from '../../api/taskQueries';
import { ErrorText } from '../../components/ui';
import { messageOf } from '../../lib/messageOf';
import type { WorkTaskDetailDto } from '../../types';
import { TaskDetailPanel } from './TaskDetailPanel';

/** The row under a task that is open: the task as soon as it has been had, why it could not be had, or a word that it is on its way. */
export function TaskRowDetail({
  id,
  colSpan,
  canManage,
  currentUserId,
  onChanged,
}: {
  id: string;
  colSpan: number;
  canManage: boolean;
  currentUserId: string | undefined;
  onChanged: (task: WorkTaskDetailDto) => void;
}) {
  const { data, isError, error } = useTask(id);
  if (data) {
    return (
      <tr>
        <td colSpan={colSpan} className="p-0">
          <TaskDetailPanel task={data} currentUserId={currentUserId} canManage={canManage} onChanged={onChanged} />
        </td>
      </tr>
    );
  }
  return (
    <tr>
      <td colSpan={colSpan} className="px-4 py-3">
        {isError ? <ErrorText>{messageOf(error, 'Could not load this task.')}</ErrorText> : <p className="text-sm text-slate-600">Loading the task…</p>}
      </td>
    </tr>
  );
}
