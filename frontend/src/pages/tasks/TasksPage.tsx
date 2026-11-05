import { useMemo, useState } from 'react';
import { useTaskList } from '../../api/taskQueries';
import { ErrorText } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import type { WorkTaskDetailDto, WorkTaskStatus } from '../../types';
import { NewTaskForm } from './NewTaskForm';
import { countByStatus, sortTasks } from './taskSorting';
import { TaskFilters } from './TaskFilters';
import { TaskStatusTabs } from './TaskStatusTabs';
import { TasksTable } from './TasksTable';

export default function TasksPage() {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const canManage = user?.role === 'Employee';

  const [statusFilter, setStatusFilter] = useState<WorkTaskStatus>('Pending');
  const [departmentFilter, setDepartmentFilter] = useState('');
  const [mineOnly, setMineOnly] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [sortBy, setSortBy] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  // Each combination of filters is a list of its own: the answer to an earlier choice can never be shown as the answer to the current one.
  const { data: tasks, isPending, isError } = useTaskList({
    departmentId: isAdmin ? departmentFilter || undefined : undefined,
    assignedToUserId: mineOnly ? user?.id : undefined,
  });
  const shown = useMemo(() => sortTasks((tasks ?? []).filter((t) => t.status === statusFilter), sortBy, sortDir), [tasks, statusFilter, sortBy, sortDir]);
  const counts = tasks ? countByStatus(tasks) : null;

  function handleSort(key: string) {
    if (sortBy === key) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else {
      setSortBy(key);
      setSortDir('asc');
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Tasks</h1>
        <p className="mt-1 text-sm text-slate-500">
          {isAdmin
            ? 'You can create and assign tasks into any branch. Otherwise you only have read-only insight — reassigning or timing an existing task stays with the staff involved.'
            : 'Quick, informal work items you and your colleagues hand to each other alongside ticket work.'}
        </p>
      </div>

      {(canManage || isAdmin) && <NewTaskForm isAdmin={isAdmin} />}

      <TaskFilters isAdmin={isAdmin} departmentId={departmentFilter} onDepartment={setDepartmentFilter} mineOnly={mineOnly} onMineOnly={setMineOnly} />

      {isError && <ErrorText>The tasks could not be loaded. Try again in a moment.</ErrorText>}
      <TaskStatusTabs active={statusFilter} counts={counts} onSelect={setStatusFilter} />
      <TasksTable
        tasks={shown}
        message={isPending ? 'Loading tasks…' : isError ? null : 'No tasks in this status yet.'}
        isAdmin={isAdmin}
        sorting={{ sortBy, sortDir, onSort: handleSort }}
        expandedId={expandedId}
        onToggle={(id) => setExpandedId((open) => (open === id ? null : id))}
        canManage={canManage}
        currentUserId={user?.id}
        // What is done to a task can change its status; the tab goes where the task went, so that it does not vanish from under the hand that did it.
        onChanged={(task: WorkTaskDetailDto) => setStatusFilter(task.status)}
      />
    </div>
  );
}
