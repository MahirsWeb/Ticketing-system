import { useMemo, useState } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import { Button, ErrorText, Field, Input, Label, StatusPill } from '../../components/ui';
import type { LookupItemFull } from '../../types';
import { LookupDialogs } from './LookupDialogs';
import { messageOf } from './settingsHelpers';
import { ListRow, ListStatus, Panel } from './settingsUi';
import { useLookupActions } from './useLookupActions';
import { useLanguage } from '../../i18n/LanguageContext';

/** A list of names that can be added, renamed, switched off and on and deleted: the help topics and the ticket categories. */
export function SimpleLookupTab({
  itemNoun,
  pluralNoun,
  maxLength,
  list,
  onCreate,
  onUpdate,
  onDelete,
  deleteWarning,
  deactivateWarning,
}: {
  itemNoun: string;
  pluralNoun?: string;
  maxLength: number;
  list: Pick<UseQueryResult<LookupItemFull[]>, 'data' | 'isPending' | 'isError' | 'isSuccess'>;
  onCreate: (name: string) => Promise<unknown>;
  onUpdate: (id: string, name: string, isActive: boolean) => Promise<unknown>;
  onDelete: (id: string) => Promise<unknown>;
  deleteWarning: string;
  deactivateWarning: string;
}) {
  const { t } = useLanguage();
  const plural = pluralNoun ?? `${itemNoun}s`;
  const data = list.data;
  const count = data?.length ?? 0;
  const [name, setName] = useState('');
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState('');
  const [editError, setEditError] = useState<string | null>(null);
  const actions = useLookupActions<LookupItemFull>({
    noun: itemNoun,
    setActive: (item, isActive) => onUpdate(item.id, item.name, isActive),
    remove: (item) => onDelete(item.id),
  });

  const filtered = useMemo(() => {
    const items = data ?? [];
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((i) => i.name.toLowerCase().includes(q));
  }, [data, search]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) return;
    try {
      await onCreate(name.trim());
      setName('');
    } catch (err) {
      setError(messageOf(err, t('lookup_addFail')));
    }
  }

  async function handleSaveEdit(item: LookupItemFull) {
    if (!editingName.trim()) return;
    setEditError(null);
    try {
      await onUpdate(item.id, editingName.trim(), item.isActive);
      setEditingId(null);
    } catch (err) {
      setEditError(messageOf(err, t('lookup_renameFail', { noun: itemNoun })));
    }
  }

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[300px_1fr]">
      <Panel className="h-fit">
        <h2 className="text-sm font-bold text-slate-800">{t('lookup_addNew', { noun: itemNoun })}</h2>
        <form onSubmit={handleCreate} className="mt-4 space-y-3">
          <Field>
            <Label>{t('common_name')}</Label>
            <Input
              placeholder={t('lookup_namePlaceholder', { noun: itemNoun })}
              value={name}
              maxLength={maxLength}
              onChange={(e) => {
                setName(e.target.value);
                setError(null);
              }}
            />
          </Field>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" className="w-full" disabled={!name.trim()}>
            {t('lookup_addBtn', { noun: itemNoun })}
          </Button>
        </form>
      </Panel>

      <Panel>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold text-slate-800">
            {t('lookup_existing', { plural })}
            {list.isSuccess ? ` (${count})` : ''}
          </h2>
          <Input aria-label={t('lookup_searchAria', { plural })} placeholder={t('lookup_searchPlaceholder', { plural })} value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        </div>
        <ErrorText>{editError ?? actions.changeError}</ErrorText>
        <ListStatus loading={list.isPending} failed={list.isError} plural={plural} />
        {list.isSuccess && (
          <div className="divide-y divide-slate-100">
            {filtered.map((item) => (
              <ListRow key={item.id}>
                {editingId === item.id ? (
                  <Input value={editingName} maxLength={maxLength} onChange={(e) => setEditingName(e.target.value)} className="max-w-xs" />
                ) : (
                  <span className="text-sm text-slate-800">{item.name}</span>
                )}
                <div className="flex items-center gap-3 text-xs">
                  {editingId === item.id ? (
                    <>
                      <button className="font-medium text-blue-700 hover:underline" onClick={() => handleSaveEdit(item)}>
                        Save
                      </button>
                      <button className="text-slate-600 hover:underline" onClick={() => setEditingId(null)}>
                        Cancel
                      </button>
                    </>
                  ) : (
                    <>
                      <StatusPill active={item.isActive} />
                      <button
                        className="font-medium text-blue-700 hover:underline"
                        onClick={() => {
                          setEditingId(item.id);
                          setEditingName(item.name);
                          setEditError(null);
                        }}
                      >
                        Rename
                      </button>
                      <button className="text-slate-600 hover:underline" onClick={() => actions.toggle(item)}>
                        {item.isActive ? t('common_deactivate') : t('common_reactivate')}
                      </button>
                      <button className="font-medium text-red-600 hover:underline" onClick={() => actions.askDelete(item)}>
                        Delete
                      </button>
                    </>
                  )}
                </div>
              </ListRow>
            ))}
            {filtered.length === 0 && <p className="py-8 text-center text-sm text-slate-600">{t('lookup_empty', { plural })}</p>}
          </div>
        )}
      </Panel>

      <LookupDialogs
        actions={actions}
        remove={{ title: (item) => `Delete "${item.name}"?`, message: () => deleteWarning }}
        deactivate={{ title: (item) => t('lookup_deactivateTitle', { name: item.name }), message: () => deactivateWarning }}
      />
    </div>
  );
}
