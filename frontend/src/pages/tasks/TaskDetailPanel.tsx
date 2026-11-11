import { useEffect, useMemo, useState } from 'react';
import { format } from 'date-fns';
import { workTasksApi } from '../../api/workTasks';
import { useEmployees } from '../../api/userQueries';
import { messageOf } from '../../lib/messageOf';
import type { WorkTaskDetailDto } from '../../types';
import { Button, ErrorText, Select } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

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
  const { t } = useLanguage();
  // The colleagues of the branch the task is in, except the one who has it.
  const colleagues = useEmployees({ departmentId: task.departmentId, enabled: canManage });
  const candidates = useMemo(
    () => (colleagues.data ?? []).filter((u) => u.id !== task.assignedToUserId).map((u) => ({ id: u.id, name: `${u.firstName} ${u.lastName}` })),
    [colleagues.data, task.assignedToUserId]
  );
  const [reassignTo, setReassignTo] = useState('');
  // The manual correction is of the LATEST work session; earlier ones are listed below and stay as they are.
  const latest = task.sessions.length > 0 ? task.sessions[task.sessions.length - 1] : null;
  const [started, setStarted] = useState(toLocalInputValue(latest?.startedAtUtc ?? null));
  const [ended, setEnded] = useState(toLocalInputValue(latest?.endedAtUtc ?? null));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const isAssignee = currentUserId === task.assignedToUserId;
  const timerRunning = task.sessions.some((s) => s.endedAtUtc === null);

  useEffect(() => {
    setStarted(toLocalInputValue(latest?.startedAtUtc ?? null));
    setEnded(toLocalInputValue(latest?.endedAtUtc ?? null));
  }, [latest?.startedAtUtc, latest?.endedAtUtc]);

  /** Does what was asked and hands the changed task on. Says whether it worked, so that what was chosen for it is only let go of when it did. */
  async function run(fn: () => Promise<WorkTaskDetailDto>): Promise<boolean> {
    setBusy(true);
    setError(null);
    try {
      onChanged(await fn());
      return true;
    } catch (err) {
      setError(messageOf(err, t('task_actionFail')));
      return false;
    } finally {
      setBusy(false);
    }
  }

  return (
    <div role="presentation" className="space-y-4 border-t border-slate-100 bg-slate-50 px-4 py-4" onClick={(e) => e.stopPropagation()}>
      <div>
        <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('task_description')}</div>
        <p className="mt-1 whitespace-pre-wrap text-sm text-slate-800">{task.description}</p>
      </div>

      {canManage && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div>
            <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('task_reassignTo')}</div>
            <div className="mt-1 flex items-center gap-2">
              <div className="w-56">
                <Select value={reassignTo} onChange={(e) => setReassignTo(e.target.value)} className="text-xs">
                  <option value="">{t('task_chooseColleague')}</option>
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
                onClick={() => run(() => workTasksApi.reassign(task.id, reassignTo)).then((worked) => worked && setReassignTo(''))}
              >
                {t('task_reassign')}
              </Button>
            </div>
            {colleagues.isError && <p className="mt-1 text-xs text-red-700">{t('task_colleaguesFail')}</p>}
          </div>

          {isAssignee && (
            <div>
              <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('task_liveTimer')}</div>
              <div className="mt-1">
                {timerRunning ? (
                  <Button type="button" variant="danger" className="text-xs" disabled={busy} onClick={() => run(() => workTasksApi.stop(task.id))}>
                    <span aria-hidden>⏹</span> {t('task_stop')}
                  </Button>
                ) : (
                  <Button type="button" className="text-xs" disabled={busy} onClick={() => run(() => workTasksApi.start(task.id))}>
                    <span aria-hidden>▶</span> {t('task_start')}
                  </Button>
                )}
              </div>
            </div>
          )}

          <div className="md:col-span-2">
            <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('task_sessionRange')}</div>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <input
                type="datetime-local"
                value={started}
                onChange={(e) => setStarted(e.target.value)}
                className="rounded-md border border-slate-300 px-2 py-1 text-xs"
              />
              <span className="text-slate-600">→</span>
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
                {t('common_save')}
              </Button>
            </div>
          </div>
        </div>
      )}

      <div>
        <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('task_workSessions')}</div>
        {task.sessions.length === 0 ? (
          <p className="mt-1 text-xs text-slate-600">{t('task_noWork')}</p>
        ) : (
          <ul className="mt-1 space-y-1 text-xs text-slate-600">
            {task.sessions.map((s) => (
              <li key={s.id}>
                <span className="font-medium text-slate-800">{s.userName}</span> — {format(new Date(s.startedAtUtc), 'dd.MM.yyyy HH:mm')} →{' '}
                {s.endedAtUtc ? format(new Date(s.endedAtUtc), 'HH:mm') : 'running'}
              </li>
            ))}
          </ul>
        )}
      </div>

      <div>
        <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('task_assignmentHistory')}</div>
        <ul className="mt-1 space-y-1 text-xs text-slate-600">
          {task.history.map((h, i) => (
            <li key={i}>
              <span className="font-medium text-slate-800">{h.assignedByName}</span> {t('task_historyAssignedTo')}{' '}
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
