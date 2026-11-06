import { useState } from 'react';
import { usersApi } from '../../api/users';
import { ErrorText } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { CreatedUserResponse, UserListItemDto } from '../../types';
import { TempPasswordModal } from './TempPasswordModal';

const messageOf = (err: unknown, fallback: string) => (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback;

/**
 * What an administrator can do to an account besides editing it: switch it off and on, give it a new temporary password, export everything
 * the system holds about the person and anonymize them. The ones that cannot be undone, or that lock somebody out, ask first. Whatever
 * fails says why, in the words of the server when it had some.
 */
export function UserAdminActions({ user, isSelf, onChanged }: { user: UserListItemDto; isSelf: boolean; onChanged: () => void }) {
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
      setError(messageOf(err, 'Could not change the account.'));
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
      setError(messageOf(err, 'Could not generate a new password.'));
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
      setError('The data could not be exported.');
    }
  }

  async function handleAnonymize() {
    setConfirmAnonymize(false);
    setError(null);
    try {
      await usersApi.anonymize(user.id);
      onChanged();
    } catch {
      setError('The account could not be anonymized.');
    }
  }

  return (
    <>
      <div className="mt-5 flex flex-wrap gap-x-5 gap-y-2 border-t border-slate-100 pt-4 text-xs">
        <button className="font-medium text-slate-600 hover:underline" onClick={handleToggleActive}>
          {user.isActive ? 'Deactivate account' : 'Reactivate account'}
        </button>
        <button className="font-medium text-blue-700 hover:underline" onClick={() => setConfirmRegenerate(true)}>
          Regenerate temp password
        </button>
        <button className="font-medium text-slate-600 hover:underline" onClick={handleExport}>
          Export personal data
        </button>
        {!isSelf && !user.email.endsWith('@anonymized.invalid') && (
          <button className="font-medium text-red-700 hover:underline" onClick={() => setConfirmAnonymize(true)}>
            Anonymize…
          </button>
        )}
      </div>
      {error && <ErrorText>{error}</ErrorText>}

      {result && <TempPasswordModal result={result} onClose={() => setResult(null)} />}
      {confirmRegenerate && (
        <ConfirmDialog
          title="Regenerate temporary password?"
          message={`This immediately invalidates ${user.firstName} ${user.lastName}'s current password and emails them a new temporary one. Are you sure?`}
          confirmLabel="Regenerate"
          danger
          onConfirm={handleRegenerate}
          onCancel={() => setConfirmRegenerate(false)}
        />
      )}
      {confirmAnonymize && (
        <ConfirmDialog
          title="Anonymize this person?"
          message={`The name, e-mail address, telephone number and picture of ${user.firstName} ${user.lastName} will be removed for good and the account locked. Their tickets, messages and work time stay, under the name "Obrisan korisnik". This cannot be undone.`}
          confirmLabel="Anonymize"
          danger
          onConfirm={handleAnonymize}
          onCancel={() => setConfirmAnonymize(false)}
        />
      )}
      {confirmDeactivate && (
        <ConfirmDialog
          title="Deactivate this account?"
          message={`${user.firstName} ${user.lastName} will no longer be able to log in and will stop receiving any emails from the app. You can reactivate this account at any time.`}
          confirmLabel="Deactivate"
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
