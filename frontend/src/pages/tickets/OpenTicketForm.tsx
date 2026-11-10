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
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';

export function OpenTicketForm({ ticket, onOpened }: { ticket: TicketDetailDto; onOpened: (t: TicketDetailDto) => void }) {
  const { t } = useLanguage();
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
      setError(t('open_allRequired'));
      return;
    }
    if (subBranches.length > 0 && !subBranchId) {
      setError(t('open_selectSub'));
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
      setError(err?.response?.data?.message ?? t('open_fail'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">Open ticket</h2>
      <form onSubmit={handleSubmit} className="grid grid-cols-2 gap-4">
        <SelectField label={t('form_ticketSource')} value={source} onChange={(v) => setSource(v as TicketSource)} options={SOURCES.map((s) => ({ value: s, label: t(`source_${s}` as TranslationKey) }))} />
        <SelectField label={t('detail_helpTopic')} value={helpTopicId} onChange={setHelpTopicId} options={lists.helpTopics.map((t) => ({ value: t.id, label: t.name }))} />
        <SelectField label={t('detail_branch')} value={departmentId} onChange={setDepartmentId} placeholder={t('newTicket_selectBranchOption')} options={lists.departments.map((d) => ({ value: d.id, label: d.name }))} />
        {subBranches.length > 0 && (
          <SelectField label={t('detail_subBranch')} value={subBranchId} onChange={setSubBranchId} placeholder={t('transfer_subOption')} options={subBranches.map((s) => ({ value: s.id, label: s.name }))} />
        )}
        <SelectField
          label={t('detail_slaPlan')}
          value={slaPlanId}
          onChange={setSlaPlanId}
          options={lists.slaPlans.map((s) => ({ value: s.id, label: `${s.name} (${s.resolutionTimeHours}h resolution)` }))}
        />
        <SelectField label={t('table_priority')} value={priority} onChange={(v) => setPriority(v as TicketPriority)} options={PRIORITIES.map((p) => ({ value: p, label: t(`priority_${p}` as TranslationKey) }))} />
        <DueDateField value={dueDate} onChange={setDueDate} />
        <Field>
          <Label>{t('form_assignTo')}</Label>
          <Select value={assignedToUserId} onChange={(e) => setAssignedToUserId(e.target.value)}>
            <option value="">{t('form_selectAgent')}</option>
            {assignees.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </Select>
          {employees.isError && <ErrorText>The list of agents could not be loaded.</ErrorText>}
        </Field>
        <SelectField
          label={t('form_category')}
          value={categoryId}
          onChange={setCategoryId}
          placeholder={t('form_noCategory')}
          options={lists.categories.map((c) => ({ value: c.id, label: c.name }))}
        />
        <div className="col-span-2">
          <ErrorText>{error}</ErrorText>
          <Button type="submit" disabled={loading}>
            {loading ? t('open_opening') : t('open_submit')}
          </Button>
        </div>
      </form>
    </Card>
  );
}
