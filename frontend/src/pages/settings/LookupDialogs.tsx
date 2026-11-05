import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { Lookup, LookupActions } from './useLookupActions';

/** The two questions of `useLookupActions`, with the words each list has for them. A refusal to delete is shown in the question that was asked. */
export function LookupDialogs<T extends Lookup>({
  actions,
  deactivate,
  remove,
}: {
  actions: LookupActions<T>;
  deactivate: { title: (item: T) => string; message: (item: T) => string };
  remove?: { title: (item: T) => string; message: (item: T) => string };
}) {
  const { confirmDeactivate, confirmDelete, deleteError } = actions;
  return (
    <>
      {confirmDelete && remove && (
        <ConfirmDialog
          title={remove.title(confirmDelete)}
          message={`${remove.message(confirmDelete)}${deleteError ? `\n\n${deleteError}` : ''}`}
          confirmLabel="Delete"
          danger
          onConfirm={actions.deleteNow}
          onCancel={actions.cancelDelete}
        />
      )}
      {confirmDeactivate && (
        <ConfirmDialog
          title={deactivate.title(confirmDeactivate)}
          message={deactivate.message(confirmDeactivate)}
          confirmLabel="Deactivate"
          danger
          onConfirm={actions.deactivateNow}
          onCancel={actions.cancelDeactivate}
        />
      )}
    </>
  );
}
