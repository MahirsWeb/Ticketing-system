import { useEffect, useState } from 'react';
import { format } from 'date-fns';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto, TicketWorkSessionDto } from '../../types';
import { Button, ErrorText, Label } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';

function toDatePart(date: Date | null) {
  if (!date) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function toTimePart(date: Date | null) {
  if (!date) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function combine(datePart: string, timePart: string): Date | null {
  if (!datePart || !timePart) return null;
  const d = new Date(`${datePart}T${timePart}:00`);
  return Number.isNaN(d.getTime()) ? null : d;
}

function formatDuration(milliseconds: number) {
  const minutes = Math.max(0, Math.round(milliseconds / 60000));
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `${h}h ${m}min` : `${m}min`;
}

function durationOf(session: TicketWorkSessionDto, now: number) {
  const end = session.endedAtUtc ? new Date(session.endedAtUtc).getTime() : now;
  return end - new Date(session.startedAtUtc).getTime();
}

/// Always-24h time input (native <input type="time"> renders AM/PM per OS locale, so we build our own).
function TimeInput({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const [draft, setDraft] = useState(value);

  // The form is cleared from outside after a save or a cancel; what is typed here has to follow.
  useEffect(() => setDraft(value), [value]);

  function commit(raw: string) {
    if (raw.trim() === '') {
      setDraft('');
      onChange('');
      return;
    }
    const match = raw.trim().match(/^(\d{1,2}):?(\d{0,2})$/);
    if (!match) {
      setDraft(value);
      return;
    }
    const h = Math.min(23, parseInt(match[1], 10) || 0);
    const min = Math.min(59, parseInt(match[2] || '0', 10) || 0);
    const formatted = `${String(h).padStart(2, '0')}:${String(min).padStart(2, '0')}`;
    setDraft(formatted);
    onChange(formatted);
  }

  return (
    <input
      type="text"
      inputMode="numeric"
      placeholder="00:00"
      value={draft}
      onChange={(e) => setDraft(e.target.value)}
      onFocus={() => setDraft(value)}
      onBlur={(e) => commit(e.target.value)}
      className="w-16 rounded-md border border-slate-300 px-2 py-1.5 text-center text-sm tabular-nums focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600"
    />
  );
}

const dateInputClass =
  'rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600';

/// A ticket's work log: any number of sessions (the morning and the afternoon are two). They are the main assignee's work.
/// People added as extra help log no time of their own.
export function WorkTimeEditor({ ticket, onChanged }: { ticket: TicketDetailDto; onChanged: (t: TicketDetailDto) => void }) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [startDate, setStartDate] = useState('');
  const [startTime, setStartTime] = useState('');
  const [endDate, setEndDate] = useState('');
  const [endTime, setEndTime] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [toDelete, setToDelete] = useState<TicketWorkSessionDto | null>(null);

  const now = Date.now();
  const total = ticket.workSessions.reduce((sum, s) => sum + durationOf(s, now), 0);
  const hasMainWorker = !!ticket.assignedToUserId;
  const startedAt = combine(startDate, startTime);
  const endedAt = combine(endDate, endTime);
  const draftDuration = startedAt && endedAt && endedAt > startedAt ? formatDuration(endedAt.getTime() - startedAt.getTime()) : null;

  function resetForm() {
    setEditingId(null);
    setStartDate('');
    setStartTime('');
    setEndDate('');
    setEndTime('');
  }

  function beginEdit(session: TicketWorkSessionDto) {
    const start = new Date(session.startedAtUtc);
    const end = session.endedAtUtc ? new Date(session.endedAtUtc) : null;
    setEditingId(session.id);
    setStartDate(toDatePart(start));
    setStartTime(toTimePart(start));
    setEndDate(toDatePart(end));
    setEndTime(toTimePart(end));
    setError(null);
  }

  async function run(action: () => Promise<TicketDetailDto>, failure: string) {
    setSaving(true);
    setError(null);
    try {
      onChanged(await action());
      return true;
    } catch (err: any) {
      setError(err?.response?.data?.message ?? failure);
      return false;
    } finally {
      setSaving(false);
    }
  }

  async function handleSave() {
    if (!startedAt) {
      setError('Enter when the work started.');
      return;
    }
    const started = startedAt.toISOString();
    const ended = endedAt ? endedAt.toISOString() : null;
    const ok = await run(
      () => (editingId ? ticketsApi.updateWorkSession(ticket.id, editingId, started, ended) : ticketsApi.addWorkSession(ticket.id, started, ended)),
      'Could not save the work session.'
    );
    if (ok) resetForm();
  }

  async function handleEndNow(session: TicketWorkSessionDto) {
    await run(
      () => ticketsApi.updateWorkSession(ticket.id, session.id, session.startedAtUtc, new Date().toISOString()),
      'Could not end the work session.'
    );
  }

  async function handleDelete(session: TicketWorkSessionDto) {
    setToDelete(null);
    const ok = await run(() => ticketsApi.deleteWorkSession(ticket.id, session.id), 'Could not delete the work session.');
    if (ok && editingId === session.id) resetForm();
  }

  return (
    <div className="rounded-lg border border-slate-100 bg-slate-50/60 p-3">
      <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
        <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Work time</div>
        {ticket.workSessions.length > 0 && <div className="text-xs font-medium text-slate-500">Total: {formatDuration(total)}</div>}
      </div>

      {ticket.workSessions.length === 0 ? (
        <p className="mb-3 text-sm text-slate-400">No work logged yet.</p>
      ) : (
        <ul className="mb-3 divide-y divide-slate-100 rounded-md border border-slate-100 bg-white text-sm">
          {ticket.workSessions.map((s) => (
            <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
              <div>
                <span className="font-medium text-slate-700">{s.userName}</span>
                <span className="ml-2 tabular-nums text-slate-500">
                  {format(new Date(s.startedAtUtc), 'dd.MM.yyyy HH:mm')} –{' '}
                  {s.endedAtUtc ? format(new Date(s.endedAtUtc), 'dd.MM.yyyy HH:mm') : <span className="font-semibold text-green-700">running</span>}
                </span>
                <span className="ml-2 text-xs text-slate-400">{formatDuration(durationOf(s, now))}</span>
              </div>
              <div className="flex items-center gap-1">
                {!s.endedAtUtc && (
                  <Button type="button" variant="secondary" className="px-2 py-1 text-xs" disabled={saving} onClick={() => handleEndNow(s)}>
                    End now
                  </Button>
                )}
                <Button type="button" variant="ghost" className="px-2 py-1 text-xs" disabled={saving} onClick={() => beginEdit(s)}>
                  Edit
                </Button>
                <Button type="button" variant="ghost" className="px-2 py-1 text-xs text-red-600" disabled={saving} onClick={() => setToDelete(s)}>
                  Delete
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {hasMainWorker ? (
        <div>
          <div className="mb-1 text-xs text-slate-500">
            {editingId ? 'Change this session' : `Log work by ${ticket.assignedToName}`}
          </div>
          <div className="flex flex-wrap items-end gap-4">
            <div>
              <Label>Started</Label>
              <div className="flex items-center gap-1.5">
                <input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} className={dateInputClass} />
                <TimeInput value={startTime} onChange={setStartTime} />
              </div>
            </div>
            <div>
              <Label>Ended (empty = still working)</Label>
              <div className="flex items-center gap-1.5">
                <input type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} className={dateInputClass} />
                <TimeInput value={endTime} onChange={setEndTime} />
              </div>
            </div>
            <Button type="button" variant="secondary" className="text-xs" disabled={saving} onClick={handleSave}>
              {saving ? 'Saving…' : editingId ? 'Save changes' : 'Add session'}
            </Button>
            {editingId && (
              <Button type="button" variant="ghost" className="text-xs" disabled={saving} onClick={resetForm}>
                Cancel
              </Button>
            )}
            {draftDuration && <span className="text-xs font-medium text-slate-500">Duration: {draftDuration}</span>}
          </div>
          <p className="mt-2 text-xs text-slate-400">
            Time is logged for the main assignee only. People added as extra help are listed on the ticket but log no time of their own.
          </p>
        </div>
      ) : (
        <p className="text-xs text-slate-400">Assign the ticket to somebody to log work time on it.</p>
      )}

      <ErrorText>{error}</ErrorText>

      {toDelete && (
        <ConfirmDialog
          title="Delete this work session?"
          message={`${toDelete.userName}, ${format(new Date(toDelete.startedAtUtc), 'dd.MM.yyyy HH:mm')}. This cannot be undone.`}
          confirmLabel="Delete"
          danger
          onConfirm={() => handleDelete(toDelete)}
          onCancel={() => setToDelete(null)}
        />
      )}
    </div>
  );
}
