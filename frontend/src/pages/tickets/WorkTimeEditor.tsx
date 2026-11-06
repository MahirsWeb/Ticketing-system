import { format } from 'date-fns';
import type { TicketDetailDto } from '../../types';
import { ErrorText } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { WorkSessionForm } from './workTime/WorkSessionForm';
import { WorkSessionList } from './workTime/WorkSessionList';
import { useNow, useWorkTimeForm } from './workTime/useWorkTimeForm';
import { durationOf, formatDuration } from './workTime/workTimeModel';

/// A ticket's work log: any number of sessions (the morning and the afternoon are two). They are the main assignee's work.
/// People added as extra help log no time of their own.
export function WorkTimeEditor({ ticket, onChanged }: { ticket: TicketDetailDto; onChanged: (t: TicketDetailDto) => void }) {
  const form = useWorkTimeForm(ticket.id, ticket.workSessions, onChanged);
  const now = useNow(ticket.workSessions.some((s) => !s.endedAtUtc));
  const total = ticket.workSessions.reduce((sum, s) => sum + durationOf(s, now), 0);
  const { toDelete } = form;

  return (
    <div className="rounded-lg border border-slate-100 bg-slate-50/60 p-3">
      <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
        <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">Work time</div>
        {ticket.workSessions.length > 0 && <div className="text-xs font-medium text-slate-600">Total: {formatDuration(total)}</div>}
      </div>

      <WorkSessionList sessions={ticket.workSessions} now={now} saving={form.saving} onEnd={form.endNow} onEdit={form.beginEdit} onDelete={form.setToDelete} />

      {ticket.assignedToUserId ? (
        <WorkSessionForm form={form} workerName={ticket.assignedToName ?? null} />
      ) : (
        <p className="text-xs text-slate-600">Assign the ticket to somebody to log work time on it.</p>
      )}

      <ErrorText>{form.error}</ErrorText>

      {toDelete && (
        <ConfirmDialog
          title="Delete this work session?"
          message={`${toDelete.userName}, ${format(new Date(toDelete.startedAtUtc), 'dd.MM.yyyy HH:mm')}. This cannot be undone.`}
          confirmLabel="Delete"
          danger
          onConfirm={() => form.deleteConfirmed(toDelete)}
          onCancel={() => form.setToDelete(null)}
        />
      )}
    </div>
  );
}
