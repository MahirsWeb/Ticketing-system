import { useEffect, useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { ticketsApi } from '../../api/tickets';
import type { DepartmentItemFull, SubBranchItemFull, TicketDetailDto } from '../../types';
import { Button, ErrorText, Select } from '../../components/ui';

export function TransferBranchForm({ ticket, onTransferred }: { ticket: TicketDetailDto; onTransferred: (t: TicketDetailDto) => void }) {
  const [expanded, setExpanded] = useState(false);
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [departmentId, setDepartmentId] = useState('');
  const [subBranches, setSubBranches] = useState<SubBranchItemFull[]>([]);
  const [subBranchId, setSubBranchId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!expanded) return;
    lookupsApi.departments().then((depts) => setDepartments(depts.filter((d) => d.id !== ticket.departmentId)));
  }, [expanded, ticket.departmentId]);

  // A branch that has sub-branches needs one chosen too. The answer for a branch that is no longer the chosen one is thrown away.
  useEffect(() => {
    setSubBranchId('');
    if (!departmentId) {
      setSubBranches([]);
      return;
    }
    let current = true;
    lookupsApi
      .subBranches(departmentId)
      .then((list) => current && setSubBranches(list))
      .catch(() => current && setSubBranches([]));
    return () => {
      current = false;
    };
  }, [departmentId]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!departmentId) {
      setError('Select a destination branch.');
      return;
    }
    if (subBranches.length > 0 && !subBranchId) {
      setError('Select a sub-branch of the destination branch.');
      return;
    }
    setLoading(true);
    try {
      const updated = await ticketsApi.transferBranch(ticket.id, departmentId, subBranchId || undefined);
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
        className="text-xs text-slate-500 hover:text-slate-600 hover:underline"
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
        {subBranches.length > 0 && (
          <div className="w-52">
            <Select value={subBranchId} onChange={(e) => setSubBranchId(e.target.value)} className="text-xs" aria-label="Destination sub-branch">
              <option value="">Select a sub-branch…</option>
              {subBranches.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </div>
        )}
        <Button type="submit" variant="secondary" disabled={loading} className="text-xs">
          {loading ? 'Transferring…' : 'Transfer'}
        </Button>
        <button type="button" onClick={() => setExpanded(false)} className="text-xs text-slate-500 hover:underline">
          Cancel
        </button>
      </form>
      <ErrorText>{error}</ErrorText>
    </div>
  );
}
