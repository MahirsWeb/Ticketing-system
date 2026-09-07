import { useEffect, useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { ticketsApi } from '../../api/tickets';
import type { DepartmentItemFull, TicketDetailDto } from '../../types';
import { Button, ErrorText, Select } from '../../components/ui';

export function TransferBranchForm({ ticket, onTransferred }: { ticket: TicketDetailDto; onTransferred: (t: TicketDetailDto) => void }) {
  const [expanded, setExpanded] = useState(false);
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [departmentId, setDepartmentId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!expanded) return;
    lookupsApi.departments().then((depts) => setDepartments(depts.filter((d) => d.id !== ticket.departmentId)));
  }, [expanded, ticket.departmentId]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!departmentId) {
      setError('Select a destination branch.');
      return;
    }
    setLoading(true);
    try {
      const updated = await ticketsApi.transferBranch(ticket.id, departmentId);
      setDepartmentId('');
      setExpanded(false);
      onTransferred(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not transfer this ticket.');
    } finally {
      setLoading(false);
    }
  }

  if (!expanded) {
    return (
      <button
        type="button"
        onClick={() => setExpanded(true)}
        className="text-xs text-slate-400 hover:text-slate-600 hover:underline"
      >
        Transfer to another branch…
      </button>
    );
  }

  return (
    <div className="rounded-md border border-slate-200 bg-slate-50 p-3">
      <form onSubmit={handleSubmit} className="flex flex-wrap items-end gap-2">
        <div className="w-52">
          <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)} className="text-xs">
            <option value="">Select destination branch…</option>
            {departments.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </Select>
        </div>
        <Button type="submit" variant="secondary" disabled={loading} className="text-xs">
          {loading ? 'Transferring…' : 'Transfer'}
        </Button>
        <button type="button" onClick={() => setExpanded(false)} className="text-xs text-slate-400 hover:underline">
          Cancel
        </button>
      </form>
      <ErrorText>{error}</ErrorText>
    </div>
  );
}
