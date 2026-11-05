import { useEffect, useState } from 'react';
import { usersApi } from '../../api/users';
import { Button, Card, ErrorText, Input } from '../../components/ui';
import type { ClientLookupResult } from '../../types';

/**
 * The first step: who the client is, found by e-mail. When the page was opened from an e-mail the sender is looked for at once (`autoLookupEmail`);
 * when no account is found for them the person is told to search by hand. Whoever is found, or nobody, is reported through `onClient`.
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
  const [email, setEmail] = useState(initialEmail);
  const [error, setError] = useState<string | null>(null);
  const [looking, setLooking] = useState(false);

  useEffect(() => {
    if (!autoLookupEmail) return;
    let current = true;
    usersApi
      .lookupByEmail(autoLookupEmail)
      .then((found) => current && onClient(found))
      .catch(() => current && setError('Could not automatically find a client account for this email — search manually below.'));
    return () => {
      current = false;
    };
  }, [autoLookupEmail, onClient]);

  async function handleLookup(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLooking(true);
    try {
      onClient(await usersApi.lookupByEmail(email));
    } catch (err: any) {
      onClient(null);
      setError(err?.response?.data?.message ?? 'No client found with that email.');
    } finally {
      setLooking(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-500">1. Find the client</h2>
      <form onSubmit={handleLookup} className="flex gap-2">
        <Input type="email" required placeholder="client@company.com" value={email} onChange={(e) => setEmail(e.target.value)} />
        <Button type="submit" disabled={looking}>
          {looking ? 'Looking…' : 'Find client'}
        </Button>
      </form>
      <ErrorText>{error}</ErrorText>
      {client && (
        <div className="mt-3 rounded-md bg-green-50 p-3 text-sm text-green-900">
          <div className="font-semibold">
            {client.firstName} {client.lastName}
          </div>
          <div>{client.email}</div>
          <div>{client.companyName ?? 'No company on file'}</div>
          <div>{client.phoneNumber ?? 'No phone number on file'}</div>
        </div>
      )}
    </Card>
  );
}
