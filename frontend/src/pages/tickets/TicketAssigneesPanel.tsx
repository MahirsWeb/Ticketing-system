import { useState } from 'react';
import { useEmployees } from '../../api/userQueries';
import { ticketsApi } from '../../api/tickets';
import { messageOf } from '../../lib/messageOf';
import type { TicketDetailDto } from '../../types';
import { Button, ErrorText, Select } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

/// Lets staff add a co-assignee (e.g. a second person who resolved a different part of the ticket) or remove one.
export function TicketAssigneesPanel({ ticket, onChanged }: { ticket: TicketDetailDto; onChanged: (t: TicketDetailDto) => void }) {
  const { t } = useLanguage();
  // The staff of the branch (or sub-branch) the ticket is in. Another branch is another list, so the answer for the one the ticket was in before is
  // never offered for the one it is in now; nothing is asked for while the ticket is in no branch.
  const colleagues = useEmployees({ departmentId: ticket.departmentId ?? undefined, subBranchId: ticket.subBranchId ?? undefined, enabled: !!ticket.departmentId });
  const [selectedUserId, setSelectedUserId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const assignedIds = new Set(ticket.assignees.map((a) => a.userId));
  const available = (colleagues.data ?? []).filter((c) => !assignedIds.has(c.id));

  async function handleAdd() {
    if (!selectedUserId) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await ticketsApi.addAssignee(ticket.id, selectedUserId);
      setSelectedUserId('');
      onChanged(updated);
    } catch (err) {
      setError(messageOf(err, t('assignee_addFail')));
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
    } catch (err) {
      setError(messageOf(err, t('assignee_removeFail')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('assignee_title')}</div>
      <div className="mt-1 flex flex-wrap items-center gap-2">
        {ticket.assignees.length === 0 && <span className="text-sm text-slate-600">{t('assignee_unassigned')}</span>}
        {ticket.assignees.map((a) => (
          <span key={a.userId} className="flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-700">
            {a.name}
            <span className="text-[10px] font-semibold uppercase text-slate-600">{a.userId === ticket.assignedToUserId ? t('assignee_worksOnIt') : t('assignee_helps')}</span>
            <button type="button" aria-label={t('assignee_remove', { name: a.name })} disabled={busy} onClick={() => handleRemove(a.userId)} className="font-bold text-slate-600 hover:text-red-600">
              ×
            </button>
          </span>
        ))}
      </div>
      {ticket.assignees.length > 1 && (
        <p className="mt-1 text-xs text-slate-600">{t('assignee_helperNote')}</p>
      )}
      {available.length > 0 && (
        <div className="mt-2 flex items-center gap-2">
          <div className="w-52">
            <Select aria-label={t('assignee_addAria')} value={selectedUserId} onChange={(e) => setSelectedUserId(e.target.value)} className="text-xs">
              <option value="">{t('assignee_addOption')}</option>
              {available.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.firstName} {u.lastName}
                </option>
              ))}
            </Select>
          </div>
          <Button type="button" variant="secondary" className="text-xs" disabled={!selectedUserId || busy} onClick={handleAdd}>
            {t('common_add')}
          </Button>
        </div>
      )}
      {colleagues.isError && <ErrorText>{t('assignee_loadFail')}</ErrorText>}
      <ErrorText>{error}</ErrorText>
    </div>
  );
}
