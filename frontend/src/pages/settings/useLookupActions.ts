import { useState } from 'react';
import { messageOf } from './settingsHelpers';
import { useLanguage } from '../../i18n/LanguageContext';

export interface Lookup {
  id: string;
  name: string;
  isActive: boolean;
}

/**
 * What can be done to an entry of any of the lists (branches, sub-branches, help topics, categories, SLA plans) besides adding and renaming it:
 * switch it off (after a question, because it disappears from the pickers), switch it back on (without one) and delete it (after a question,
 * and a refusal from the server, which has rules about what tickets already use, is shown in the question). Whatever fails says why, in the
 * server's words when there are some.
 */
export function useLookupActions<T extends Lookup>({
  noun,
  setActive,
  remove,
}: {
  noun: string;
  setActive: (item: T, isActive: boolean) => Promise<unknown>;
  /** Absent for what is only ever switched off (a sub-branch). */
  remove?: (item: T) => Promise<unknown>;
}) {
  const { t } = useLanguage();
  const [confirmDeactivate, setConfirmDeactivate] = useState<T | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<T | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [changeError, setChangeError] = useState<string | null>(null);

  async function changeStatus(item: T, isActive: boolean) {
    setChangeError(null);
    try {
      await setActive(item, isActive);
    } catch (err) {
      setChangeError(messageOf(err, t('lookup_changeFail', { noun })));
    }
  }

  return {
    confirmDeactivate,
    confirmDelete,
    deleteError,
    changeError,
    /** Deactivating asks first; reactivating does not. */
    toggle(item: T) {
      if (item.isActive) setConfirmDeactivate(item);
      else void changeStatus(item, true);
    },
    cancelDeactivate: () => setConfirmDeactivate(null),
    async deactivateNow() {
      if (!confirmDeactivate) return;
      const item = confirmDeactivate;
      setConfirmDeactivate(null);
      await changeStatus(item, false);
    },
    askDelete: (item: T) => setConfirmDelete(item),
    cancelDelete() {
      setConfirmDelete(null);
      setDeleteError(null);
    },
    async deleteNow() {
      if (!confirmDelete || !remove) return;
      setDeleteError(null);
      try {
        await remove(confirmDelete);
        setConfirmDelete(null);
      } catch (err) {
        setDeleteError(messageOf(err, t('lookup_deleteFail', { noun })));
      }
    },
  };
}

export type LookupActions<T extends Lookup> = ReturnType<typeof useLookupActions<T>>;
