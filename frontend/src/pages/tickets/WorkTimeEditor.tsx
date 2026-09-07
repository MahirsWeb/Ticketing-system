import { useState } from 'react';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto } from '../../types';
import { Button, ErrorText, Label } from '../../components/ui';

function toDatePart(iso: string | null) {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function toTimePart(iso: string | null) {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function combine(datePart: string, timePart: string): Date | null {
  if (!datePart || !timePart) return null;
  const d = new Date(`${datePart}T${timePart}:00`);
  return Number.isNaN(d.getTime()) ? null : d;
}

function formatDuration(start: Date, end: Date) {
  const minutes = Math.max(0, Math.round((end.getTime() - start.getTime()) / 60000));
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `${h}h ${m}min` : `${m}min`;
}

/// Always-24h time input (native <input type="time"> renders AM/PM per OS locale, so we build our own).
function TimeInput({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const [draft, setDraft] = useState(value);

  function commit(raw: string) {
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

/// Manually logged work session — not tied to any single assignee, just a record of when work happened.
export function WorkTimeEditor({ ticket, onChanged }: { ticket: TicketDetailDto; onChanged: (t: TicketDetailDto) => void }) {
  const [startDate, setStartDate] = useState(toDatePart(ticket.workStartedAtUtc));
  const [startTime, setStartTime] = useState(toTimePart(ticket.workStartedAtUtc));
  const [endDate, setEndDate] = useState(toDatePart(ticket.workEndedAtUtc));
  const [endTime, setEndTime] = useState(toTimePart(ticket.workEndedAtUtc));
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const startedAt = combine(startDate, startTime);
  const endedAt = combine(endDate, endTime);
  const duration = startedAt && endedAt && endedAt > startedAt ? formatDuration(startedAt, endedAt) : null;

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const updated = await ticketsApi.setWorkTime(ticket.id, startedAt ? startedAt.toISOString() : null, endedAt ? endedAt.toISOString() : null);
      onChanged(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not save work time.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="rounded-lg border border-slate-100 bg-slate-50/60 p-3">
      <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Work time</div>
      <div className="flex flex-wrap items-end gap-4">
        <div>
          <Label>Started</Label>
          <div className="flex items-center gap-1.5">
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600"
            />
            <TimeInput value={startTime} onChange={setStartTime} />
          </div>
        </div>
        <div>
          <Label>Ended</Label>
          <div className="flex items-center gap-1.5">
            <input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
              className="rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600"
            />
            <TimeInput value={endTime} onChange={setEndTime} />
          </div>
        </div>
        <Button type="button" variant="secondary" className="text-xs" disabled={saving} onClick={handleSave}>
          {saving ? 'Saving…' : 'Save'}
        </Button>
        {duration && <span className="text-xs font-medium text-slate-500">Duration: {duration}</span>}
      </div>
      <ErrorText>{error}</ErrorText>
    </div>
  );
}
