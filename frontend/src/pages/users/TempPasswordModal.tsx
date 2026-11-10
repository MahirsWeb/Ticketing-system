import type { CreatedUserResponse } from '../../types';
import { Button } from '../../components/ui';
import { Modal } from '../../components/Modal';
import { format, formatDistanceToNow } from 'date-fns';
import { useLanguage } from '../../i18n/LanguageContext';

export function TempPasswordModal({ result, onClose }: { result: CreatedUserResponse; onClose: () => void }) {
  const { t } = useLanguage();
  const expiresAt = new Date(result.temporaryPasswordExpiresAtUtc);
  return (
    <Modal title={t('temppw_title')} onClose={onClose} maxWidth="max-w-md">
        <p className="mb-4 text-sm text-slate-600">
          {t('temppw_emailedTo')} <span className="font-semibold">{result.email}</span>.
          {result.temporaryPassword !== null && ' ' + t('temppw_shownHere')} {t('temppw_expiresAt')}{' '}
          <span className="font-semibold">{format(expiresAt, 'HH:mm:ss')}</span>{' '}
          ({formatDistanceToNow(expiresAt, { addSuffix: true })}).
        </p>
        {result.temporaryPassword !== null ? (
          <div className="mb-4 rounded-md border border-dashed border-slate-300 bg-slate-50 p-4 text-center font-mono text-xl tracking-wider text-slate-900">
            {result.temporaryPassword}
          </div>
        ) : (
          <p className="mb-4 rounded-md border border-slate-200 bg-slate-50 p-4 text-center text-sm text-slate-600">
            {t('temppw_byEmailOnly')}
          </p>
        )}
        <Button className="w-full" onClick={onClose}>
          {t('common_done')}
        </Button>
    </Modal>
  );
}
