import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Modal } from '../../components/Modal';
import { Button } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import { ClientPicker, type ClientPick } from './ClientPicker';
import { OnBehalfTicketFields } from './OnBehalfTicketFields';
import { useOnBehalfTicket } from './useOnBehalfTicket';

type MadeTicket = { id: string; ticketNumber: string };

/**
 * The form for a new ticket, as the dialog has it: the same picker and the same fields as the page, with the state in this component, so that it begins from
 * nothing each time the form is shown (a new idempotency key with it: with the old one a second ticket would quietly be the first again).
 */
function QuickTicketForm({ onDirtyChange, onCreated }: { onDirtyChange: (dirty: boolean) => void; onCreated: (ticket: MadeTicket) => void }) {
  const [pick, setPick] = useState<ClientPick>({ companyId: null, clientId: null });
  const model = useOnBehalfTicket({ clientId: pick.clientId, prefill: undefined, emailMessageId: null, onCreated });
  useEffect(() => {
    onDirtyChange(model.isDirty);
  }, [model.isDirty, onDirtyChange]);

  return (
    <div className="space-y-4">
      <ClientPicker value={pick} onChange={setPick} />
      {pick.clientId && <OnBehalfTicketFields model={model} bare />}
    </div>
  );
}

/**
 * A ticket opened for a client from anywhere in the application, in a dialog, so that the person who is working on something else does not leave it.
 * Esc, and the button that closes it, ask "discard what you typed?" when there is text or a file in the form, instead of losing it. After the ticket
 * is made the person is still where they were: the dialog says so, with a link to the ticket, and offers another one.
 */
export function QuickTicketDialog({ onClose }: { onClose: () => void }) {
  const { t } = useLanguage();
  const [round, setRound] = useState(0);
  const [dirty, setDirty] = useState(false);
  const [made, setMade] = useState<MadeTicket | null>(null);
  const [confirming, setConfirming] = useState(false);

  function requestClose() {
    if (!made && dirty) setConfirming(true);
    else onClose();
  }

  return (
    <>
      <Modal
        title={t('quick_title')}
        onClose={requestClose}
        maxWidth="max-w-3xl"
        scroll
        headerAction={
          <button
            type="button"
            onClick={requestClose}
            aria-label={t('quick_close')}
            className="rounded-md px-2 text-xl font-bold leading-none text-slate-600 hover:bg-slate-100 hover:text-slate-900"
          >
            <span aria-hidden>×</span>
          </button>
        }
      >
        {made ? (
          <div role="status" className="space-y-4">
            <p className="text-sm text-slate-800">{t('quick_createdMessage', { number: made.ticketNumber })}</p>
            <div className="flex flex-wrap items-center gap-3">
              <Link to={`/tickets/${made.id}`} onClick={onClose} className="text-sm font-semibold text-blue-700 underline">
                {t('quick_openTicket', { number: made.ticketNumber })}
              </Link>
              <Button
                variant="secondary"
                onClick={() => {
                  setMade(null);
                  setDirty(false);
                  setRound((r) => r + 1);
                }}
              >
                {t('quick_another')}
              </Button>
              <Button variant="secondary" onClick={onClose}>
                {t('quick_close')}
              </Button>
            </div>
          </div>
        ) : (
          <QuickTicketForm
            key={round}
            onDirtyChange={setDirty}
            onCreated={(ticket) => {
              setMade(ticket);
              setDirty(false);
            }}
          />
        )}
      </Modal>
      {confirming && (
        <ConfirmDialog
          title={t('quick_discardTitle')}
          message={t('quick_discardMessage')}
          confirmLabel={t('quick_discard')}
          danger
          onConfirm={onClose}
          onCancel={() => setConfirming(false)}
        />
      )}
    </>
  );
}
