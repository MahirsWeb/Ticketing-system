import { useState } from 'react';
import { usersApi } from '../../api/users';
import { ErrorText } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { CreatedUserResponse, UserListItemDto } from '../../types';
import { TempPasswordModal } from './TempPasswordModal';
import { useLanguage } from '../../i18n/LanguageContext';

const messageOf = (err: unknown, fallback: string) => (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback;

/**
 * What an administrator can do to an account besides editing it: switch it off and on, give it a new temporary password, export everything
 * the system holds about the person and anonymize them. The ones that cannot be undone, or that lock somebody out, ask first. Whatever
 * fails says why, in the words of the server when it had some.
 */
export function UserAdminActions({ user, isSelf, onChanged }: { user: UserListItemDto; isSelf: boolean; onChanged: () => void }) {
  const { t } = useLanguage();
  const [result, setResult] = useState<CreatedUserResponse | null>(null);
  const [confirmRegenerate, setConfirmRegenerate] = useState(false);
  const [confirmDeactivate, setConfirmDeactivate] = useState(false);
  const [confirmAnonymize, setConfirmAnonymize] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function changeActive(isActive: boolean) {
    setError(null);
    try {
      await usersApi.setActive(user.id, isActive);
      onChanged();
    } catch (err) {
      setError(messageOf(err, t('useradmin_changeFail')));
    }
  }

  function handleToggleActive() {
    if (user.isActive) setConfirmDeactivate(true);
    else void changeActive(true);
  }

  async function handleRegenerate() {
    setConfirmRegenerate(false);
    setError(null);
    try {
      setResult(await usersApi.regenerateTempPassword(user.id));
    } catch (err) {
      setError(messageOf(err, t('useradmin_genFail')));
    }
  }

  async function handleExport() {
    setError(null);
    try {
      const blob = await usersApi.exportData(user.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `user-${user.id}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      setError(t('useradmin_exportFail'));
    }
  }

  async function handleAnonymize() {
    setConfirmAnonymize(false);
    setError(null);
    try {
      await usersApi.anonymize(user.id);
      onChanged();
    } catch {
      setError(t('useradmin_anonFail'));
    }
  }

  return (
    <>
      <div className="mt-5 flex flex-wrap gap-x-5 gap-y-2 border-t border-slate-100 pt-4 text-xs">
        <button className="font-medium text-slate-600 hover:underline" onClick={handleToggleActive}>
          {user.isActive ? t('useradmin_deactivate') : t('useradmin_reactivate')}
        </button>
        <button className="font-medium text-blue-700 hover:underline" onClick={() => setConfirmRegenerate(true)}>
          {t('useradmin_regenerate')}
        </button>
        <button className="font-medium text-slate-600 hover:underline" onClick={handleExport}>
          {t('useradmin_export')}
        </button>
        {!isSelf && !user.email.endsWith('@anonymized.invalid') && (
          <button className="font-medium text-red-700 hover:underline" onClick={() => setConfirmAnonymize(true)}>
            {t('useradmin_anonymize')}
          </button>
        )}
      </div>
      {error && <ErrorText>{error}</ErrorText>}

      {result && <TempPasswordModal result={result} onClose={() => setResult(null)} />}
      {confirmRegenerate && (
        <ConfirmDialog
          title={t('useradmin_regenerateTitle')}
          message={t('useradmin_regenerateMsg', { name: `${user.firstName} ${user.lastName}` })}
          confirmLabel={t('useradmin_regenerateConfirm')}
          danger
          onConfirm={handleRegenerate}
          onCancel={() => setConfirmRegenerate(false)}
        />
      )}
      {confirmAnonymize && (
        <ConfirmDialog
          title={t('useradmin_anonTitle')}
          message={t('useradmin_anonMsg', { name: `${user.firstName} ${user.lastName}` })}
          confirmLabel={t('useradmin_anonConfirm')}
          danger
          onConfirm={handleAnonymize}
          onCancel={() => setConfirmAnonymize(false)}
        />
      )}
      {confirmDeactivate && (
        <ConfirmDialog
          title={t('useradmin_deactTitle')}
          message={t('useradmin_deactMsg', { name: `${user.firstName} ${user.lastName}` })}
          confirmLabel={t('useradmin_deactConfirm')}
          danger
          onConfirm={() => {
            setConfirmDeactivate(false);
            void changeActive(false);
          }}
          onCancel={() => setConfirmDeactivate(false)}
        />
      )}
    </>
  );
}
