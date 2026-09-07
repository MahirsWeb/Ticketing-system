import { useEffect, useState } from 'react';
import { usersApi } from '../../api/users';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto, UserListItemDto } from '../../types';
import { Button, ErrorText, Select } from '../../components/ui';

/// Lets staff add a co-assignee (e.g. a second person who resolved a different part of the ticket) or remove one.
export function TicketAssigneesPanel({ ticket, onChanged }: { ticket: TicketDetailDto; onChanged: (t: TicketDetailDto) => void }) {
  const [candidates, setCandidates] = useState<UserListItemDto[]>([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!ticket.departmentId) return;
    const subBranchId = ticket.subBranchId ?? undefined;
    usersApi.list({ role: 'Employee', departmentId: ticket.departmentId, subBranchId }).then(setCandidates);
  }, [ticket.departmentId, ticket.subBranchId]);

  const assignedIds = new Set(ticket.assignees.map((a) => a.userId));
  const available = candidates.filter((c) => !assignedIds.has(c.id));

  async function handleAdd() {
    if (!selectedUserId) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await ticketsApi.addAssignee(ticket.id, selectedUserId);
      setSelectedUserId('');
      onChanged(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not add assignee.');
    } finally {
      setBusy(false);
    }
  }

  async function handleRemove(userId: string) {
    setBusy(true);
    setError(null);
    try {
      const updated = await ticketsApi.removeAssignee(ticket.id, userId);
      onChanged(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not remove assignee.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Assignees</div>
      <div className="mt-1 flex flex-wrap items-center gap-2">
        {ticket.assignees.length === 0 && <span className="text-sm text-slate-400">Unassigned</span>}
        {ticket.assignees.map((a) => (
          <span key={a.userId} className="flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-700">
            {a.name}
            <button type="button" disabled={busy} onClick={() => handleRemove(a.userId)} className="font-bold text-slate-400 hover:text-red-600">
              ×
            </button>
          </span>
        ))}
      </div>
      {available.length > 0 && (
        <div className="mt-2 flex items-center gap-2">
          <div className="w-52">
            <Select value={selectedUserId} onChange={(e) => setSelectedUserId(e.target.value)} className="text-xs">
              <option value="">Add a co-assignee…</option>
              {available.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.firstName} {u.lastName}
                </option>
              ))}
            </Select>
          </div>
          <Button type="button" variant="secondary" className="text-xs" disabled={!selectedUserId || busy} onClick={handleAdd}>
            Add
          </Button>
        </div>
      )}
      <ErrorText>{error}</ErrorText>
    </div>
  );
}
