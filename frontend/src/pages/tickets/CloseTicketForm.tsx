import { useState } from 'react';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto } from '../../types';
import { Button, Card, ErrorText, Label, Textarea } from '../../components/ui';

export function CloseTicketForm({ ticket, onClosed }: { ticket: TicketDetailDto; onClosed: (t: TicketDetailDto) => void }) {
  const [resolutionSummary, setResolutionSummary] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!resolutionSummary.trim()) {
      setError('A detailed resolution summary is required to close the ticket.');
      return;
    }
    setLoading(true);
    try {
      const updated = await ticketsApi.close(ticket.id, resolutionSummary);
      onClosed(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not close the ticket.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">Close ticket</h2>
      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <Label>Resolution summary *</Label>
          <Textarea rows={4} value={resolutionSummary} onChange={(e) => setResolutionSummary(e.target.value)} placeholder="What was the issue and how was it resolved?" />
        </div>
        <ErrorText>{error}</ErrorText>
        <Button type="submit" variant="danger" disabled={loading}>
          {loading ? 'Closing…' : 'Close ticket'}
        </Button>
      </form>
    </Card>
  );
}
