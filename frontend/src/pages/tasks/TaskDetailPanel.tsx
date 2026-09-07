import { useEffect, useState } from 'react';
import { format } from 'date-fns';
import { workTasksApi } from '../../api/workTasks';
import { usersApi } from '../../api/users';
import type { WorkTaskDetailDto } from '../../types';
import { Button, ErrorText, Select } from '../../components/ui';

function toLocalInputValue(iso: string | null) {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/// Expandable row detail for a task: description, handoff history, and (for staff) reassignment,
/// the live start/stop timer (assignee only), and a manual time correction.
export function TaskDetailPanel({
  task,
  currentUserId,
  canManage,
  onChanged,
}: {
  task: WorkTaskDetailDto;
  currentUserId: string | undefined;
  canManage: boolean;
  onChanged: (t: WorkTaskDetailDto) => void;
}) {
  const [candidates, setCandidates] = useState<{ id: string; name: string }[]>([]);
  const [reassignTo, setReassignTo] = useState('');
  const [started, setStarted] = useState(toLocalInputValue(task.startedAtUtc));
  const [ended, setEnded] = useState(toLocalInputValue(task.endedAtUtc));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const isAssignee = currentUserId === task.assignedToUserId;
  const timerRunning = !!task.startedAtUtc && !task.endedAtUtc;

  useEffect(() => {
    setStarted(toLocalInputValue(task.startedAtUtc));
    setEnded(toLocalInputValue(task.endedAtUtc));
  }, [task.startedAtUtc, task.endedAtUtc]);

  useEffect(() => {
    if (!canManage) return;
    usersApi.list({ role: 'Employee', departmentId: task.departmentId }).then((employees) =>
      setCandidates(
        employees.filter((u) => u.id !== task.assignedToUserId).map((u) => ({ id: u.id, name: `${u.firstName} ${u.lastName}` }))
      )
    );
  }, [canManage, task.departmentId, task.assignedToUserId]);

  async function run(fn: () => Promise<WorkTaskDetailDto>) {
    setBusy(true);
    setError(null);
    try {
      onChanged(await fn());
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Action failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4 border-t border-slate-100 bg-slate-50 px-4 py-4" onClick={(e) => e.stopPropagation()}>
      <div>
        <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Description</div>
        <p className="mt-1 whitespace-pre-wrap text-sm text-slate-800">{task.description}</p>
      </div>

      {canManage && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div>
            <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Reassign to</div>
            <div className="mt-1 flex items-center gap-2">
              <div className="w-56">
                <Select value={reassignTo} onChange={(e) => setReassignTo(e.target.value)} className="text-xs">
                  <option value="">Choose a colleague…</option>
                  {candidates.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </Select>
              </div>
              <Button
                type="button"
                variant="secondary"
                className="text-xs"
                disabled={!reassignTo || busy}
                onClick={() => run(() => workTasksApi.reassign(task.id, reassignTo)).then(() => setReassignTo(''))}
              >
                Reassign
              </Button>
            </div>
          </div>

          {isAssignee && (
            <div>
              <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Live timer</div>
              <div className="mt-1">
                {timerRunning ? (
                  <Button type="button" variant="danger" className="text-xs" disabled={busy} onClick={() => run(() => workTasksApi.stop(task.id))}>
                    ⏹ Stop
                  </Button>
                ) : (
                  <Button type="button" className="text-xs" disabled={busy} onClick={() => run(() => workTasksApi.start(task.id))}>
                    ▶ Start
                  </Button>
                )}
              </div>
            </div>
          )}

          <div className="md:col-span-2">
            <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Manual start / end time</div>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <input
                type="datetime-local"
                value={started}
                onChange={(e) => setStarted(e.target.value)}
                className="rounded-md border border-slate-300 px-2 py-1 text-xs"
              />
              <span className="text-slate-400">→</span>
              <input
                type="datetime-local"
                value={ended}
                onChange={(e) => setEnded(e.target.value)}
                className="rounded-md border border-slate-300 px-2 py-1 text-xs"
              />
              <Button
                type="button"
                variant="secondary"
                className="text-xs"
                disabled={busy}
                onClick={() =>
                  run(() =>
                    workTasksApi.setTime(
                      task.id,
                      started ? new Date(started).toISOString() : null,
                      ended ? new Date(ended).toISOString() : null
                    )
                  )
                }
              >
                Save
              </Button>
            </div>
          </div>
        </div>
      )}

      <div>
        <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Assignment history</div>
        <ul className="mt-1 space-y-1 text-xs text-slate-600">
          {task.history.map((h, i) => (
            <li key={i}>
              <span className="font-medium text-slate-800">{h.assignedByName}</span> assigned to{' '}
              <span className="font-medium text-slate-800">{h.assignedToName}</span> —{' '}
              {format(new Date(h.assignedAtUtc), 'dd.MM.yyyy HH:mm')}
            </li>
          ))}
        </ul>
      </div>

      <ErrorText>{error}</ErrorText>
    </div>
  );
}
