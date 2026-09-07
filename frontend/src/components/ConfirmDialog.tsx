import { Button, Card } from './ui';

/// Small "are you sure?" modal for actions that are too easy to trigger by an accidental click.
export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Confirm',
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
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <Card className="w-full max-w-sm p-6">
        <h2 className="mb-2 text-lg font-bold text-slate-900">{title}</h2>
        <p className="mb-5 text-sm text-slate-600">{message}</p>
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onCancel}>
            Cancel
          </Button>
          <Button variant={danger ? 'danger' : 'primary'} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </div>
      </Card>
    </div>
  );
}
