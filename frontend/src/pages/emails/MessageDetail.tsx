import { Link } from 'react-router-dom';
import { format } from 'date-fns';
import { useEmailPrefill } from '../../api/emailQueries';
import type { InboxMessageSummaryDto } from '../../api/emailIntegration';
import { Card, ErrorText, Spinner } from '../../components/ui';
import { SafeHtml } from '../../components/SafeHtml';
import { useLanguage } from '../../i18n/LanguageContext';

/**
 * The message that is chosen. Each message is an entry of its own, so the text of the one that was chosen before can never be shown under
 * the sender of the one that is chosen now, and a message that cannot be loaded says so instead of waiting for ever.
 */
export function MessageDetail({ selected }: { selected: InboxMessageSummaryDto | null }) {
  const { t } = useLanguage();
  const detail = useEmailPrefill(selected?.messageId ?? null);

  return (
    <Card className="flex-1 p-5">
      {!selected ? (
        <p className="py-16 text-center text-sm text-slate-600">{t('email_selectToRead')}</p>
      ) : detail.isError ? (
        <ErrorText>{t('email_loadDetailFail')}</ErrorText>
      ) : !detail.data ? (
        <div role="status" aria-label={t('email_loadingDetailAria')} className="flex justify-center py-16">
          <Spinner className="h-6 w-6 text-blue-700" />
        </div>
      ) : (
        <div>
          <div className="mb-3 flex items-start justify-between gap-3 border-b border-slate-100 pb-3">
            <div>
              <h2 className="text-base font-bold text-slate-900">{detail.data.subject}</h2>
              <p className="mt-1 text-xs text-slate-600">
                From {selected.fromName ? `${selected.fromName} <${detail.data.clientEmail}>` : detail.data.clientEmail} ·{' '}
                {format(new Date(detail.data.receivedAtUtc), 'dd.MM.yyyy HH:mm')}
              </p>
            </div>
            {selected.isMarked && (
              <div className="shrink-0">
                {selected.convertedTicketId ? (
                  <Link to={`/tickets/${selected.convertedTicketId}`} className="text-xs font-medium text-green-700 hover:underline">
                    View ticket →
                  </Link>
                ) : (
                  <Link to={`/tickets/new-on-behalf?fromEmail=${encodeURIComponent(selected.messageId)}`} className="text-xs font-medium text-blue-700 hover:underline">
                    Open ticket →
                  </Link>
                )}
              </div>
            )}
          </div>
          <SafeHtml className="prose prose-sm max-w-none" html={detail.data.bodyHtml} />
        </div>
      )}
    </Card>
  );
}
