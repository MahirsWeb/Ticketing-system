import { useEffect, useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { ticketsApi } from '../../api/tickets';
import type { DepartmentItemFull, LookupItem, SlaPlanItem, SubBranchItemFull, TicketDetailDto, TicketPriority, TicketSource } from '../../types';
import { Button, Card, ErrorText, Label, Select } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';

const SOURCES: TicketSource[] = ['Phone', 'Email', 'TicketSystem', 'Other'];
const PRIORITIES: TicketPriority[] = ['Emergency', 'High', 'Medium', 'Low'];

export function OpenTicketForm({ ticket, onOpened }: { ticket: TicketDetailDto; onOpened: (t: TicketDetailDto) => void }) {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';

  const [helpTopics, setHelpTopics] = useState<LookupItem[]>([]);
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [subBranches, setSubBranches] = useState<SubBranchItemFull[]>([]);
  const [slaPlans, setSlaPlans] = useState<SlaPlanItem[]>([]);
  const [categories, setCategories] = useState<LookupItem[]>([]);
  const [assignees, setAssignees] = useState<{ id: string; name: string }[]>([]);

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

  useEffect(() => {
    Promise.all([lookupsApi.helpTopics(), lookupsApi.departments(), lookupsApi.slaPlans(), lookupsApi.ticketCategories()]).then(
      ([topics, depts, slas, cats]) => {
        setHelpTopics(topics);
        setDepartments(depts);
        setSlaPlans(slas);
        setCategories(cats);
        if (topics[0]) setHelpTopicId(topics[0].id);
        if (isAdmin && !departmentId && depts[0]) setDepartmentId(depts[0].id);
        if (slas[0]) setSlaPlanId(slas[0].id);
      }
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!departmentId) {
      setSubBranches([]);
      setSubBranchId('');
      return;
    }
    lookupsApi.subBranches(departmentId).then(setSubBranches);
    setSubBranchId('');
  }, [departmentId]);

  useEffect(() => {
    if (!departmentId) {
      setAssignees([]);
      return;
    }
    setAssignedToUserId('');
    usersApi.list({ role: 'Employee', departmentId, subBranchId: subBranchId || undefined }).then((employees) => {
      setAssignees(employees.map((u) => ({ id: u.id, name: `${u.firstName} ${u.lastName}` })));
    });
  }, [departmentId, subBranchId]);

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
        dueDateUtc: dueDate ? new Date(`${dueDate}T23:59:59`).toISOString() : null,
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
        <div>
          <Label>Ticket source</Label>
          <Select value={source} onChange={(e) => setSource(e.target.value as TicketSource)}>
            {SOURCES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </Select>
        </div>
        <div>
          <Label>Help topic</Label>
          <Select value={helpTopicId} onChange={(e) => setHelpTopicId(e.target.value)}>
            {helpTopics.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </Select>
        </div>
        <div>
          <Label>Branch</Label>
          <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
            <option value="">Select a branch…</option>
            {departments.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </Select>
        </div>
        {subBranches.length > 0 && (
          <div>
            <Label>Sub-branch</Label>
            <Select value={subBranchId} onChange={(e) => setSubBranchId(e.target.value)}>
              <option value="">Select a sub-branch…</option>
              {subBranches.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </div>
        )}
        <div>
          <Label>SLA plan</Label>
          <Select value={slaPlanId} onChange={(e) => setSlaPlanId(e.target.value)}>
            {slaPlans.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.resolutionTimeHours}h resolution)
              </option>
            ))}
          </Select>
        </div>
        <div>
          <Label>Priority</Label>
          <Select value={priority} onChange={(e) => setPriority(e.target.value as TicketPriority)}>
            {PRIORITIES.map((p) => (
              <option key={p} value={p}>
                {p}
              </option>
            ))}
          </Select>
        </div>
        <div>
          <Label>Due date</Label>
          <input
            type="date"
            className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm"
            value={dueDate}
            onChange={(e) => setDueDate(e.target.value)}
          />
        </div>
        <div>
          <Label>Assign to</Label>
          <Select value={assignedToUserId} onChange={(e) => setAssignedToUserId(e.target.value)}>
            <option value="">Select an agent…</option>
            {assignees.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </Select>
        </div>
        <div>
          <Label>Category (internal, staff only)</Label>
          <Select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            <option value="">No category</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </div>
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
