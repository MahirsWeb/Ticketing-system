import type { CreatedUserResponse } from '../../types';
import { Button } from '../../components/ui';
import { Modal } from '../../components/Modal';
import { format, formatDistanceToNow } from 'date-fns';

export function TempPasswordModal({ result, onClose }: { result: CreatedUserResponse; onClose: () => void }) {
  const expiresAt = new Date(result.temporaryPasswordExpiresAtUtc);
  return (
    <Modal title="Account created" onClose={onClose} maxWidth="max-w-md">
        <p className="mb-4 text-sm text-slate-600">
          An invite with a temporary password has been emailed to <span className="font-semibold">{result.email}</span>.
          {result.temporaryPassword !== null && " It's shown here too in case you need it."} It expires at{' '}
          <span className="font-semibold">{format(expiresAt, 'HH:mm:ss')}</span>{' '}
          ({formatDistanceToNow(expiresAt, { addSuffix: true })}).
        </p>
        {result.temporaryPassword !== null ? (
          <div className="mb-4 rounded-md border border-dashed border-slate-300 bg-slate-50 p-4 text-center font-mono text-xl tracking-wider text-slate-900">
            {result.temporaryPassword}
          </div>
        ) : (
          <p className="mb-4 rounded-md border border-slate-200 bg-slate-50 p-4 text-center text-sm text-slate-600">
            The password was sent only to the account owner by email.
          </p>
        )}
        <Button className="w-full" onClick={onClose}>
          Done
        </Button>
    </Modal>
  );
}
