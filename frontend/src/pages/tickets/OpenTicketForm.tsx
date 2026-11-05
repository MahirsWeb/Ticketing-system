import { useEffect, useState } from 'react';
import { useSubBranches } from '../../api/lookupQueries';
import { useEmployees } from '../../api/userQueries';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto, TicketPriority, TicketSource } from '../../types';
import { Button, Card, ErrorText, Field, Label, Select } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { DueDateField, SelectField } from './ticketFields';
import { PRIORITIES, SOURCES, dueDateToUtc } from './ticketOptions';
import { useTicketLists } from './useTicketLists';

export function OpenTicketForm({ ticket, onOpened }: { ticket: TicketDetailDto; onOpened: (t: TicketDetailDto) => void }) {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';

  const [source, setSource] = useState<TicketSource>('TicketSystem');
  const [priority, setPriority] = useState<TicketPriority>('Medium');
  const [helpTopicId, setHelpTopicId] = useState('');
  // Prefill from the branch the client already picked when submitting; otherwise fall back to the
  // staff member's own branch (non-admin) or leave it empty for Admin to choose.
  const [departmentId, setDepartmentId] = useState(ticket.departmentId ?? (isAdmin ? '' : user?.departmentId ?? ''));
  const [subBranchId, setSubBranchId] = useState('');
  const [slaPlanId, setSlaPlanId] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [assignedToUserId, setAssignedToUserId] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // The lists are shared with the other screens, and the first entry of each is chosen once (see useTicketLists). The sub-branches are asked for per
  // branch, so an answer for a branch that is no longer the chosen one is never shown: changing the branch quickly used to let the slower answer
  // replace the list for the new one.
  const lists = useTicketLists({ isAdmin, departmentId, onTopic: setHelpTopicId, onSla: setSlaPlanId, onDepartment: setDepartmentId });
  const { data: subBranches = [] } = useSubBranches(departmentId);

  useEffect(() => {
    setSubBranchId('');
  }, [departmentId]);

  // Who the ticket can be given to: the staff of the chosen branch, and of the sub-branch when one is chosen. The sub-branch only counts once the list of the
  // branch says it is one of them, so that changing the branch does not ask for the staff of a pair that does not exist; each branch is a list of its own,
  // so the answer for the one that was chosen before is never offered for the one that is chosen now.
  const effectiveSubBranchId = subBranches.some((s) => s.id === subBranchId) ? subBranchId : '';
  const employees = useEmployees({ departmentId: departmentId || undefined, subBranchId: effectiveSubBranchId || undefined, enabled: !!departmentId });
  const assignees = (employees.data ?? []).map((u) => ({ id: u.id, name: `${u.firstName} ${u.lastName}` }));

  useEffect(() => {
    setAssignedToUserId('');
  }, [departmentId, effectiveSubBranchId]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!helpTopicId || !departmentId || !slaPlanId || !assignedToUserId) {
      setError('All fields except due date are required to open a ticket.');
      return;
    }
    if (subBranches.length > 0 && !subBranchId) {
      setError('Please select a sub-branch.');
      return;
    }
    setLoading(true);
    try {
      const updated = await ticketsApi.open(ticket.id, {
        source,
        helpTopicId,
        departmentId,
        subBranchId: subBranchId || undefined,
        slaPlanId,
        priority,
        dueDateUtc: dueDateToUtc(dueDate),
        assignedToUserId,
        categoryId: categoryId || undefined,
      });
      onOpened(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not open the ticket.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">Open ticket</h2>
      <form onSubmit={handleSubmit} className="grid grid-cols-2 gap-4">
        <SelectField label="Ticket source" value={source} onChange={(v) => setSource(v as TicketSource)} options={SOURCES.map((s) => ({ value: s, label: s }))} />
        <SelectField label="Help topic" value={helpTopicId} onChange={setHelpTopicId} options={lists.helpTopics.map((t) => ({ value: t.id, label: t.name }))} />
        <SelectField label="Branch" value={departmentId} onChange={setDepartmentId} placeholder="Select a branch…" options={lists.departments.map((d) => ({ value: d.id, label: d.name }))} />
        {subBranches.length > 0 && (
          <SelectField label="Sub-branch" value={subBranchId} onChange={setSubBranchId} placeholder="Select a sub-branch…" options={subBranches.map((s) => ({ value: s.id, label: s.name }))} />
        )}
        <SelectField
          label="SLA plan"
          value={slaPlanId}
          onChange={setSlaPlanId}
          options={lists.slaPlans.map((s) => ({ value: s.id, label: `${s.name} (${s.resolutionTimeHours}h resolution)` }))}
        />
        <SelectField label="Priority" value={priority} onChange={(v) => setPriority(v as TicketPriority)} options={PRIORITIES.map((p) => ({ value: p, label: p }))} />
        <DueDateField value={dueDate} onChange={setDueDate} />
        <Field>
          <Label>Assign to</Label>
          <Select value={assignedToUserId} onChange={(e) => setAssignedToUserId(e.target.value)}>
            <option value="">Select an agent…</option>
            {assignees.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </Select>
          {employees.isError && <ErrorText>The list of agents could not be loaded.</ErrorText>}
        </Field>
        <SelectField
          label="Category (internal, staff only)"
          value={categoryId}
          onChange={setCategoryId}
          placeholder="No category"
          options={lists.categories.map((c) => ({ value: c.id, label: c.name }))}
        />
        <div className="col-span-2">
          <ErrorText>{error}</ErrorText>
          <Button type="submit" disabled={loading}>
            {loading ? 'Opening…' : 'Open ticket'}
          </Button>
        </div>
      </form>
    </Card>
  );
}
