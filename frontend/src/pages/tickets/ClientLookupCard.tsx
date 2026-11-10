import { useEffect, useRef, useState } from 'react';
import { useClientByEmail } from '../../api/userQueries';
import { usersApi } from '../../api/users';
import { messageOf } from '../../lib/messageOf';
import { Button, Card, ErrorText, Input } from '../../components/ui';
import type { ClientLookupResult } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';


/**
 * The first step: who the client is, found by e-mail. When the page was opened from an e-mail the sender is looked for at once (`autoLookupEmail`);
 * when no account is found for them the person is told to search by hand. Whoever is found, or nobody, is reported through `onClient`.
 * What the person finds by hand wins: once they have searched themselves, the answer of the automatic search is no longer heard, and neither is its failure.
 */
export function ClientLookupCard({
  client,
  onClient,
  initialEmail = '',
  autoLookupEmail,
}: {
  client: ClientLookupResult | null;
  onClient: (client: ClientLookupResult | null) => void;
  initialEmail?: string;
  autoLookupEmail?: string;
}) {
  const { t } = useLanguage();
  const [email, setEmail] = useState(initialEmail);
  const [error, setError] = useState<string | null>(null);
  const [looking, setLooking] = useState(false);
  const [byHand, setByHand] = useState(false);
  // Read when the answer arrives, which can be after the person has searched: a state would be the value of the render that asked.
  const searchedByHand = useRef(false);

  const automatic = useClientByEmail(autoLookupEmail);
  // Who is told is whoever the page names now; a page that names another one does not make the search ask again.
  const tell = useRef(onClient);
  useEffect(() => {
    tell.current = onClient;
  });
  useEffect(() => {
    if (automatic.data && !searchedByHand.current) tell.current(automatic.data);
  }, [automatic.data]);

  async function handleLookup(e: React.FormEvent) {
    e.preventDefault();
    searchedByHand.current = true;
    setByHand(true);
    setError(null);
    setLooking(true);
    try {
      onClient(await usersApi.lookupByEmail(email));
    } catch (err) {
      onClient(null);
      setError(messageOf(err, t('clookup_noClient')));
    } finally {
      setLooking(false);
    }
  }

  const shown = error ?? (automatic.isError && !byHand ? t('clookup_autoFailed') : null);

  return (
    <Card className="p-5">
      <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">{t('clookup_title')}</h2>
      <form onSubmit={handleLookup} className="flex gap-2">
        <Input type="email" required aria-label={t('clookup_emailAria')} placeholder="client@company.com" value={email} onChange={(e) => setEmail(e.target.value)} />
        <Button type="submit" disabled={looking}>
          {looking ? t('clookup_looking') : t('clookup_find')}
        </Button>
      </form>
      <ErrorText>{shown}</ErrorText>
      {client && (
        <div className="mt-3 rounded-md bg-green-50 p-3 text-sm text-green-900">
          <div className="font-semibold">
            {client.firstName} {client.lastName}
          </div>
          <div>{client.email}</div>
          <div>{client.companyName ?? t('clookup_noCompany')}</div>
          <div>{client.phoneNumber ?? t('clookup_noPhone')}</div>
        </div>
      )}
    </Card>
  );
}
