import { useMemo, useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { useDepartmentsAdmin } from '../../api/adminLookupQueries';
import { Button, ErrorText, Field, Input, Label, StatusPill } from '../../components/ui';
import type { DepartmentItemFull } from '../../types';
import { LookupDialogs } from './LookupDialogs';
import { SubBranchesInline } from './SubBranchesInline';
import { LIMITS, messageOf } from './settingsHelpers';
import { ListRow, ListStatus, Panel } from './settingsUi';
import { useLookupActions } from './useLookupActions';
import { useLanguage } from '../../i18n/LanguageContext';

/** The branches: added, renamed, switched off and on, deleted, and each with its sub-branches. */
export function BranchesTab() {
  const { t } = useLanguage();
  const departments = useDepartmentsAdmin();
  const data = departments.data;
  const count = data?.length ?? 0;
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState('');
  const [editingEmail, setEditingEmail] = useState('');
  const [editError, setEditError] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const actions = useLookupActions<DepartmentItemFull>({
    noun: t('noun_branch'),
    setActive: (d, isActive) => lookupsApi.updateDepartment(d.id, d.name, d.email, isActive),
    remove: (d) => lookupsApi.deleteDepartment(d.id),
  });

  const filtered = useMemo(() => {
    const items = data ?? [];
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((i) => i.name.toLowerCase().includes(q) || i.email.toLowerCase().includes(q));
  }, [data, search]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim() || !email.trim()) {
      setError(t('branches_bothRequired'));
      return;
    }
    try {
      await lookupsApi.createDepartment(name.trim(), email.trim());
      setName('');
      setEmail('');
    } catch (err) {
      setError(messageOf(err, t('branches_addFail')));
    }
  }

  async function handleSaveEdit(item: DepartmentItemFull) {
    if (!editingName.trim() || !editingEmail.trim()) return;
    setEditError(null);
    try {
      await lookupsApi.updateDepartment(item.id, editingName.trim(), editingEmail.trim(), item.isActive);
      setEditingId(null);
    } catch (err) {
      setEditError(messageOf(err, t('branches_updateFail')));
    }
  }

  const canAdd = !!name.trim() && !!email.trim();

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[300px_1fr]">
      <Panel className="h-fit">
        <h2 className="text-sm font-bold text-slate-800">{t('branches_addTitle')}</h2>
        <p className="mt-0.5 mb-4 text-xs text-slate-600">{t('branches_emailNote')}</p>
        <form onSubmit={handleCreate} className="space-y-3">
          <Field>
            <Label>{t('branches_nameLabel')}</Label>
            <Input
              placeholder={t('branches_namePlaceholder')}
              value={name}
              maxLength={LIMITS.name}
              onChange={(e) => {
                setName(e.target.value);
                setError(null);
              }}
            />
          </Field>
          <Field>
            <Label>{t('branches_emailLabel')}</Label>
            <Input
              type="email"
              placeholder="servis@firma.ba"
              value={email}
              maxLength={LIMITS.email}
              onChange={(e) => {
                setEmail(e.target.value);
                setError(null);
              }}
            />
          </Field>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" className="w-full" disabled={!canAdd}>
            {t('branches_addBtn')}
          </Button>
        </form>
      </Panel>

      <Panel>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold text-slate-800">{t('lookup_existing', { plural: t('noun_branches') })}{departments.isSuccess ? ` (${count})` : ''}</h2>
          <Input aria-label={t('branches_searchAria')} placeholder={t('branches_searchPlaceholder')} value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        </div>
        <ErrorText>{editError ?? actions.changeError}</ErrorText>
        <ListStatus loading={departments.isPending} failed={departments.isError} plural={t('noun_branches')} />
        {departments.isSuccess && (
          <div className="divide-y divide-slate-100">
            {filtered.map((item) => (
              <div key={item.id}>
                <ListRow>
                  {editingId === item.id ? (
                    <div className="flex flex-1 gap-2">
                      <Input value={editingName} maxLength={LIMITS.name} onChange={(e) => setEditingName(e.target.value)} className="max-w-xs" />
                      <Input type="email" value={editingEmail} maxLength={LIMITS.email} onChange={(e) => setEditingEmail(e.target.value)} className="max-w-xs" />
                    </div>
                  ) : (
                    <span className="text-sm text-slate-800">
                      {item.name} <span className="text-xs text-slate-600">— {item.email}</span>
                    </span>
                  )}
                  <div className="flex shrink-0 items-center gap-3 text-xs">
                    {editingId === item.id ? (
                      <>
                        <button className="font-medium text-blue-700 hover:underline" onClick={() => handleSaveEdit(item)}>
                          {t('common_save')}
                        </button>
                        <button className="text-slate-600 hover:underline" onClick={() => setEditingId(null)}>
                          {t('common_cancel')}
                        </button>
                      </>
                    ) : (
                      <>
                        <StatusPill active={item.isActive} />
                        <button className="text-slate-600 hover:underline" onClick={() => setExpandedId(expandedId === item.id ? null : item.id)}>
                          {expandedId === item.id ? t('branches_hideSub') : t('branches_subBranches')}
                        </button>
                        <button
                          className="font-medium text-blue-700 hover:underline"
                          onClick={() => {
                            setEditingId(item.id);
                            setEditingName(item.name);
                            setEditingEmail(item.email);
                            setEditError(null);
                          }}
                        >
                          {t('common_edit')}
                        </button>
                        <button className="text-slate-600 hover:underline" onClick={() => actions.toggle(item)}>
                          {item.isActive ? t('common_deactivate') : t('common_reactivate')}
                        </button>
                        <button className="font-medium text-red-600 hover:underline" onClick={() => actions.askDelete(item)}>
                          {t('common_delete')}
                        </button>
                      </>
                    )}
                  </div>
                </ListRow>
                {expandedId === item.id && <SubBranchesInline departmentId={item.id} />}
              </div>
            ))}
            {filtered.length === 0 && <p className="py-8 text-center text-sm text-slate-600">{t('branches_empty')}</p>}
          </div>
        )}
      </Panel>

      <LookupDialogs
        actions={actions}
        remove={{
          title: () => t('branches_deleteTitle'),
          message: (item) => t('branches_deleteMsg', { name: item.name }),
        }}
        deactivate={{
          title: () => t('branches_deactivateTitle'),
          message: (item) => t('branches_deactivateMsg', { name: item.name }),
        }}
      />
    </div>
  );
}
