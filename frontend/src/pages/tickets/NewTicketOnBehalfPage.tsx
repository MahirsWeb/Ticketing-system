import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useEmailPrefill } from '../../api/emailQueries';
import type { EmailPrefillDto } from '../../api/emailIntegration';
import { ErrorText } from '../../components/ui';
import type { ClientLookupResult } from '../../types';
import { ClientLookupCard } from './ClientLookupCard';
import { OnBehalfTicketFields } from './OnBehalfTicketFields';
import { useOnBehalfTicket } from './useOnBehalfTicket';

/** The two steps, with what the model keeps between them: the client, and what has been typed about the issue. */
function OnBehalfFlow({ prefill, emailMessageId }: { prefill: EmailPrefillDto | undefined; emailMessageId: string | null }) {
  const [client, setClient] = useState<ClientLookupResult | null>(null);
  const model = useOnBehalfTicket({ client, prefill, emailMessageId });

  return (
    <>
      {prefill && (
        <p className="rounded-md bg-blue-50 px-3 py-2 text-sm text-blue-800">
          ✉️ Title, description and sender were pulled from the marked email.
          {prefill.hasAttachments && ' Its attachments will be added to the ticket automatically once created.'}
        </p>
      )}
      <ClientLookupCard client={client} onClient={setClient} initialEmail={prefill?.clientEmail} autoLookupEmail={prefill?.clientEmail} />
      {client && <OnBehalfTicketFields model={model} />}
    </>
  );
}

/**
 * Staff open a ticket for a client who reported the issue by phone or in person, or who wrote an e-mail that was marked for it. When the page is
 * opened from an e-mail it waits for what the e-mail gives (the sender, the subject, the text) before the steps are drawn, so that they start
 * from it; if the e-mail cannot be had the person is told so and does everything by hand.
 */
export default function NewTicketOnBehalfPage() {
  const [searchParams] = useSearchParams();
  const fromEmailMessageId = searchParams.get('fromEmail');
  const prefill = useEmailPrefill(fromEmailMessageId);

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">New ticket on behalf of a client</h1>
        <p className="mt-1 text-sm text-slate-600">
          Use this when a client reports an issue by phone (or in person) instead of through the client portal.
          The ticket will show up exactly as if the client had submitted it themselves.
        </p>
      </div>

      {fromEmailMessageId && prefill.isPending ? (
        <p className="text-sm text-slate-600">Loading the e-mail…</p>
      ) : (
        <>
          {prefill.isError && <ErrorText>The e-mail could not be loaded. Search for the client below and describe the issue by hand.</ErrorText>}
          <OnBehalfFlow key={fromEmailMessageId ?? ''} prefill={prefill.data} emailMessageId={fromEmailMessageId} />
        </>
      )}
    </div>
  );
}
