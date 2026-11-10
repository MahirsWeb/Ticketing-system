import { useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { useSubBranchesAdmin } from '../../api/adminLookupQueries';
import { Button, ErrorText, Input, StatusPill } from '../../components/ui';
import type { SubBranchItemFull } from '../../types';
import { LookupDialogs } from './LookupDialogs';
import { LIMITS, messageOf } from './settingsHelpers';
import { ListRow } from './settingsUi';
import { useLookupActions } from './useLookupActions';
import { useLanguage } from '../../i18n/LanguageContext';

/** The finer units of one branch, shown under it on request. */
export function SubBranchesInline({ departmentId }: { departmentId: string }) {
  const { t } = useLanguage();
  const subBranches = useSubBranchesAdmin(departmentId);
  const items = subBranches.data ?? [];
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const actions = useLookupActions<SubBranchItemFull>({
    noun: 'sub-branch',
    setActive: (item, isActive) => lookupsApi.updateSubBranch(item.id, item.name, isActive),
  });

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) return;
    try {
      await lookupsApi.createSubBranch(departmentId, name.trim());
      setName('');
    } catch (err) {
      setError(messageOf(err, t('sub_addFail')));
    }
  }

  if (subBranches.isPending) return <p className="py-2 text-xs text-slate-600">{t('sub_loading')}</p>;
  if (subBranches.isError) return <ErrorText>{t('sub_loadFail')}</ErrorText>;

  return (
    <div className="mt-1 mb-2 rounded-lg border border-slate-100 bg-slate-50/60 p-3">
      <p className="mb-2 text-xs text-slate-600">
        Optional finer units within this branch, sharing its email but tracked separately for statistics and
        ticket assignment. If any exist, staff and tickets in this branch must pick one.
      </p>
      <form onSubmit={handleCreate} className="mb-2 flex gap-2">
        <Input
          placeholder={t('sub_namePlaceholder')}
          value={name}
          maxLength={LIMITS.name}
          onChange={(e) => {
            setName(e.target.value);
            setError(null);
          }}
          className="max-w-xs text-xs"
        />
        <Button type="submit" variant="secondary" className="text-xs" disabled={!name.trim()}>
          Add
        </Button>
      </form>
      <ErrorText>{error ?? actions.changeError}</ErrorText>
      <div className="divide-y divide-slate-100">
        {items.map((item) => (
          <ListRow key={item.id}>
            <span className="text-sm text-slate-700">{item.name}</span>
            <div className="flex items-center gap-2">
              <StatusPill active={item.isActive} />
              <button className="text-xs text-slate-600 hover:underline" onClick={() => actions.toggle(item)}>
                {item.isActive ? t('common_deactivate') : t('common_reactivate')}
              </button>
            </div>
          </ListRow>
        ))}
        {items.length === 0 && <p className="py-2 text-xs text-slate-600">No sub-branches — this branch is used as a single unit.</p>}
      </div>

      <LookupDialogs
        actions={actions}
        deactivate={{
          title: () => t('sub_deactivateTitle'),
          message: (item) =>
            `Tickets and staff already assigned to "${item.name}" keep that assignment and are completely unaffected — this does not change any existing tickets or statistics. It just removes "${item.name}" from the picker when opening a new ticket or assigning staff. If this is the branch's only active sub-branch, the branch will behave as if it has none until you reactivate it.`,
        }}
      />
    </div>
  );
}
