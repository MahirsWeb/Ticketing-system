import { useEffect, useMemo, useState } from 'react';
import { useDepartments, useSubBranches } from '../../api/lookupQueries';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto } from '../../types';
import { Button, ErrorText, Select } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

export function TransferBranchForm({ ticket, onTransferred }: { ticket: TicketDetailDto; onTransferred: (t: TicketDetailDto) => void }) {
  const { t } = useLanguage();
  const [expanded, setExpanded] = useState(false);
  const [departmentId, setDepartmentId] = useState('');
  const [subBranchId, setSubBranchId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // The branches are fetched once for the whole application (and only when the form is opened); this ticket's own branch is not a destination.
  const { data: allDepartments = [] } = useDepartments({ enabled: expanded });
  const departments = useMemo(() => allDepartments.filter((d) => d.id !== ticket.departmentId), [allDepartments, ticket.departmentId]);

  // A branch that has sub-branches needs one chosen too. The list is asked for per branch, so an answer for a branch that is no longer the chosen one is never shown.
  const { data: subBranches = [] } = useSubBranches(departmentId);
  useEffect(() => {
    setSubBranchId('');
  }, [departmentId]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!departmentId) {
      setError(t('transfer_selectDest'));
      return;
    }
    if (subBranches.length > 0 && !subBranchId) {
      setError(t('transfer_selectSub'));
      return;
    }
    setLoading(true);
    try {
      const updated = await ticketsApi.transferBranch(ticket.id, departmentId, subBranchId || undefined);
      setDepartmentId('');
      setExpanded(false);
      onTransferred(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? t('transfer_fail'));
    } finally {
      setLoading(false);
    }
  }

  if (!expanded) {
    return (
      <button
        type="button"
        onClick={() => setExpanded(true)}
        className="text-xs text-slate-600 hover:text-slate-800 hover:underline"
      >
        {t('transfer_open')}
      </button>
    );
  }

  return (
    <div className="rounded-md border border-slate-200 bg-slate-50 p-3">
      <form onSubmit={handleSubmit} className="flex flex-wrap items-end gap-2">
        <div className="w-52">
          <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)} className="text-xs">
            <option value="">{t('transfer_destOption')}</option>
            {departments.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </Select>
        </div>
        {subBranches.length > 0 && (
          <div className="w-52">
            <Select value={subBranchId} onChange={(e) => setSubBranchId(e.target.value)} className="text-xs" aria-label={t('transfer_subAria')}>
              <option value="">{t('transfer_subOption')}</option>
              {subBranches.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </div>
        )}
        <Button type="submit" variant="secondary" disabled={loading} className="text-xs">
          {loading ? t('transfer_transferring') : t('transfer_submit')}
        </Button>
        <button type="button" onClick={() => setExpanded(false)} className="text-xs text-slate-600 hover:underline">
          {t('common_cancel')}
        </button>
      </form>
      <ErrorText>{error}</ErrorText>
    </div>
  );
}
