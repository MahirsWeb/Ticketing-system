import { useEffect, useState } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { messageOf } from '../../../lib/messageOf';
import type { TicketDetailDto, TicketWorkSessionDto } from '../../../types';
import { TIME_MESSAGE, combine, formatDuration, parseTime, toDatePart, toTimePart } from './workTimeModel';

const END_MESSAGE = 'Enter both the date and the time when the work ended, or leave both empty if it is still going.';

/** How often the time of a session that is running is counted again. */
const TICK_MS = 30_000;

/** The time now, for what is counted up to now: renewed when something starts to run, and while it runs. Nothing that has ended depends on it. */
export function useNow(counting: boolean) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!counting) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), TICK_MS);
    return () => window.clearInterval(timer);
  }, [counting]);
  return now;
}

/**
 * The form that adds a session or changes one, and what is done with the sessions of the ticket (end, delete). The fields hold what was
 * typed, and are read when the form is sent: nothing is taken from a field in any other way, so a time that was typed a moment ago is
 * never missed, and a time that is not a time is said to be so instead of being turned into another.
 */
export function useWorkTimeForm(ticketId: string, sessions: TicketWorkSessionDto[], onChanged: (t: TicketDetailDto) => void) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [startDate, setStartDate] = useState('');
  const [startTime, setStartTime] = useState('');
  const [endDate, setEndDate] = useState('');
  const [endTime, setEndTime] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [toDelete, setToDelete] = useState<TicketWorkSessionDto | null>(null);

  const startedAt = combine(startDate, parseTime(startTime) ?? '');
  const endedAt = combine(endDate, parseTime(endTime) ?? '');
  const draftDuration = startedAt && endedAt && endedAt > startedAt ? formatDuration(endedAt.getTime() - startedAt.getTime()) : null;

  function resetForm() {
    setEditingId(null);
    setStartDate('');
    setStartTime('');
    setEndDate('');
    setEndTime('');
  }

  // A session that somebody else removed cannot be changed any more.
  const editedStillThere = editingId === null || sessions.some((s) => s.id === editingId);
  useEffect(() => {
    if (!editedStillThere) resetForm();
  }, [editedStillThere]);

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

  function cancelEdit() {
    resetForm();
    setError(null);
  }

  async function run(action: () => Promise<TicketDetailDto>, failure: string) {
    setSaving(true);
    setError(null);
    try {
      onChanged(await action());
      return true;
    } catch (err) {
      setError(messageOf(err, failure));
      return false;
    } finally {
      setSaving(false);
    }
  }

  /** Why the form cannot be sent, or null. */
  function problem() {
    const start = parseTime(startTime);
    const end = parseTime(endTime);
    if (start === null || end === null) return TIME_MESSAGE;
    if (!startedAt) return 'Enter when the work started.';
    // An end that is half given is not "still going": that is what an empty end means.
    if (!!endDate !== !!end) return END_MESSAGE;
    return null;
  }

  async function submit(e: { preventDefault: () => void }) {
    e.preventDefault();
    const issue = problem();
    if (issue || !startedAt) {
      setError(issue);
      return;
    }
    const started = startedAt.toISOString();
    const ended = endedAt ? endedAt.toISOString() : null;
    const ok = await run(
      () => (editingId ? ticketsApi.updateWorkSession(ticketId, editingId, started, ended) : ticketsApi.addWorkSession(ticketId, started, ended)),
      'Could not save the work session.'
    );
    if (ok) resetForm();
  }

  async function endNow(session: TicketWorkSessionDto) {
    await run(() => ticketsApi.updateWorkSession(ticketId, session.id, session.startedAtUtc, new Date().toISOString()), 'Could not end the work session.');
  }

  async function deleteConfirmed(session: TicketWorkSessionDto) {
    setToDelete(null);
    const ok = await run(() => ticketsApi.deleteWorkSession(ticketId, session.id), 'Could not delete the work session.');
    if (ok && editingId === session.id) resetForm();
  }

  return {
    editingId, startDate, startTime, endDate, endTime, error, saving, toDelete, draftDuration,
    setStartDate, setStartTime, setEndDate, setEndTime, setToDelete,
    beginEdit, cancelEdit, submit, endNow, deleteConfirmed,
  };
}
