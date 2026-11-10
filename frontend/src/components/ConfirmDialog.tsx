import { Button } from './ui';
import { Modal } from './Modal';
import { useLanguage } from '../i18n/LanguageContext';

/// Small "are you sure?" modal for actions that are too easy to trigger by an accidental click.
export function ConfirmDialog({
  title,
  message,
  confirmLabel,
  danger,
  onConfirm,
  onCancel,
}: {
  title: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const { t } = useLanguage();
  return (
    <Modal title={title} onClose={onCancel}>
      <p className="mb-5 whitespace-pre-line text-sm text-slate-600">{message}</p>
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          {t('common_cancel')}
        </Button>
        <Button variant={danger ? 'danger' : 'primary'} onClick={onConfirm}>
          {confirmLabel ?? t('common_confirm')}
        </Button>
      </div>
    </Modal>
  );
}
