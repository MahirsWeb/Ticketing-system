import { useState } from 'react';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto } from '../../types';
import { Button, Card, ErrorText, Label, Textarea, Field } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

export function CloseTicketForm({ ticket, onClosed }: { ticket: TicketDetailDto; onClosed: (t: TicketDetailDto) => void }) {
  const { t } = useLanguage();
  const [resolutionSummary, setResolutionSummary] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!resolutionSummary.trim()) {
      setError(t('close_summaryRequired'));
      return;
    }
    setLoading(true);
    try {
      const updated = await ticketsApi.close(ticket.id, resolutionSummary);
      onClosed(updated);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? t('close_fail'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">{t('close_title')}</h2>
      <form onSubmit={handleSubmit} className="space-y-4">
        <Field>
          <Label>{t('close_summaryLabel')}</Label>
          <Textarea rows={4} value={resolutionSummary} onChange={(e) => setResolutionSummary(e.target.value)} placeholder={t('close_summaryPlaceholder')} />
        </Field>
        <ErrorText>{error}</ErrorText>
        <Button type="submit" variant="danger" disabled={loading}>
          {loading ? t('close_closing') : t('close_title')}
        </Button>
      </form>
    </Card>
  );
}
