import { useMemo, useState } from 'react';
import { useEmployees } from '../../api/userQueries';
import { workTasksApi } from '../../api/workTasks';
import { messageOf } from '../../lib/messageOf';
import { useAuthStore } from '../../store/authStore';
import type { UserListItemDto } from '../../types';
import { Button, Card, ErrorText, Field, Input, Label, Select, SuccessText, Textarea } from '../../components/ui';

/**
 * The form for a new task. An administrator can give it to anybody; an employee to a colleague of their own branch, or to themselves.
 * The lists the page shows are asked again by the API layer when a task is made.
 */
export function NewTaskForm({ isAdmin }: { isAdmin: boolean }) {
  const user = useAuthStore((s) => s.user);
  const branchId = isAdmin ? undefined : (user?.departmentId ?? undefined);
  // An employee who is in no branch has nobody to choose from, and the server is not asked.
  const inNoBranch = !isAdmin && !branchId;
  const colleagues = useEmployees({ departmentId: branchId, enabled: !inNoBranch });

  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [assignedToUserId, setAssignedToUserId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState(false);
  const [loading, setLoading] = useState(false);

  const colleaguesByBranch = useMemo(() => {
    const groups = new Map<string, UserListItemDto[]>();
    for (const c of colleagues.data ?? []) {
      const key = c.departmentName ?? 'No branch';
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(c);
    }
    return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [colleagues.data]);

  /** Writing anything new takes back "Task created.": it is about what was sent, not about what is being written. */
  const edit = <T,>(set: (value: T) => void) => (value: T) => {
    setCreated(false);
    set(value);
  };

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setCreated(false);
    const cleanTitle = title.trim();
    const cleanDescription = description.trim();
    if (!cleanTitle || !cleanDescription || !assignedToUserId) {
      setError('Title, description and assignee are all required.');
      return;
    }
    setLoading(true);
    try {
      await workTasksApi.create(cleanTitle, cleanDescription, assignedToUserId);
      setTitle('');
      setDescription('');
      setAssignedToUserId('');
      setCreated(true);
    } catch (err) {
      setError(messageOf(err, 'Could not create the task.'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">New task</h2>
      <form onSubmit={handleCreate} className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Field>
          <Label>Title</Label>
          <Input value={title} maxLength={150} onChange={(e) => edit(setTitle)(e.target.value)} placeholder="e.g. Napravi plan za iduću sedmicu" />
        </Field>
        <Field>
          <Label>Assign to</Label>
          <Select value={assignedToUserId} onChange={(e) => edit(setAssignedToUserId)(e.target.value)}>
            <option value="">Choose a colleague{isAdmin ? '' : ' (or yourself)'}…</option>
            {colleaguesByBranch.map(([branchName, group]) => (
              <optgroup key={branchName} label={branchName}>
                {group.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.firstName} {c.lastName}
                    {c.id === user?.id ? ' — me' : ''}
                  </option>
                ))}
              </optgroup>
            ))}
          </Select>
          {inNoBranch && <p className="mt-1 text-xs text-slate-600">You are not in a branch, so there are no colleagues to choose from.</p>}
          {colleagues.isError && <ErrorText>The list of colleagues could not be loaded.</ErrorText>}
        </Field>
        <Field className="md:col-span-2">
          <Label>Description</Label>
          <Textarea rows={3} value={description} onChange={(e) => edit(setDescription)(e.target.value)} placeholder="What needs to be done?" />
        </Field>
        <div className="md:col-span-2">
          <ErrorText>{error}</ErrorText>
          {created && <SuccessText>Task created.</SuccessText>}
          <Button type="submit" disabled={loading}>
            {loading ? 'Creating…' : 'Create task'}
          </Button>
        </div>
      </form>
    </Card>
  );
}
