import { useState } from 'react';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto, TicketStatus } from '../../types';
import { Button, ErrorText } from '../../components/ui';

type LiveStatus = Extract<TicketStatus, 'Open' | 'InProgress' | 'Resolved'>;

// Mirrors TicketWorkflow on the server. The server is the authority and rejects anything else; this only decides
// which buttons are worth showing. Closing has its own form (a resolution summary is required).
const MOVES: Record<LiveStatus, Array<{ to: LiveStatus; label: string }>> = {
  Open: [
    { to: 'InProgress', label: 'Start work' },
    { to: 'Resolved', label: 'Mark resolved' },
  ],
  InProgress: [
    { to: 'Resolved', label: 'Mark resolved' },
    { to: 'Open', label: 'Back to open' },
  ],
  Resolved: [
    { to: 'InProgress', label: 'Back to in progress' },
    { to: 'Open', label: 'Back to open' },
  ],
};

/** Lets staff move a live ticket between Open, In progress and Resolved. */
export function TicketStatusActions({ ticket, onChanged }: { ticket: TicketDetailDto; onChanged: (t: TicketDetailDto) => void }) {
  const [busy, setBusy] = useState<LiveStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  const moves = MOVES[ticket.status as LiveStatus];
  if (!moves) return null;

  async function move(to: LiveStatus) {
    setBusy(to);
    setError(null);
    try {
      onChanged(await ticketsApi.setStatus(ticket.id, to));
    } catch (err) {
      const message = (err as { response?: { data?: { message?: string } } }).response?.data?.message;
      setError(message ?? 'Could not change the status.');
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <div className="flex flex-wrap items-center gap-2">
        {moves.map((m) => (
          <Button key={m.to} variant="secondary" className="px-3 py-1.5 text-xs" disabled={busy !== null} onClick={() => move(m.to)}>
            {busy === m.to ? 'Saving…' : m.label}
          </Button>
        ))}
      </div>
      <ErrorText>{error}</ErrorText>
    </div>
  );
}
