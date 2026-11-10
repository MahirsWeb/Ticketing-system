import clsx from 'clsx';
import { formatDistanceToNow } from 'date-fns';
import { Card } from '../../components/ui';
import type { InboxMessageSummaryDto } from '../../api/emailIntegration';
import { useLanguage } from '../../i18n/LanguageContext';

/**
 * The messages of the tab that is chosen. The whole row can be clicked with a mouse; the button with the subject is what a keyboard (or a screen
 * reader) uses. `message` is what is said when there are no rows (that none are marked, or that they are on their way), and nothing when there is
 * nothing to say.
 */
export function MessageList({
  messages,
  message,
  selectedId,
  onSelect,
  onToggleMark,
}: {
  messages: InboxMessageSummaryDto[];
  message: string | null;
  selectedId: string | null;
  onSelect: (messageId: string) => void;
  onToggleMark: (m: InboxMessageSummaryDto) => void;
}) {
  const { t } = useLanguage();
  return (
    <Card className="w-96 shrink-0 divide-y divide-slate-100 overflow-y-auto" style={{ maxHeight: '32rem' }}>
      {messages.length === 0 && message && <p className="px-4 py-8 text-center text-sm text-slate-600">{message}</p>}
      {messages.map((m) => (
        <div
          key={m.messageId}
          role="presentation"
          onClick={() => onSelect(m.messageId)}
          className={clsx('flex cursor-pointer items-start gap-2 px-3 py-2 hover:bg-slate-50', selectedId === m.messageId && 'bg-blue-50 hover:bg-blue-50')}
        >
          <input
            type="checkbox"
            checked={m.isMarked}
            onClick={(e) => e.stopPropagation()}
            onChange={() => onToggleMark(m)}
            className="mt-1 h-3.5 w-3.5 shrink-0"
            title={t('email_markAsTicket')}
            aria-label={`Mark "${m.subject}" as a ticket`}
          />
          <div className="min-w-0 flex-1">
            <div className="flex items-center justify-between gap-2">
              <span className="truncate text-xs font-semibold text-slate-800">{m.fromName || m.fromEmail}</span>
              <span className="shrink-0 text-[11px] text-slate-600">{formatDistanceToNow(new Date(m.receivedAtUtc), { addSuffix: true })}</span>
            </div>
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                onSelect(m.messageId);
              }}
              aria-current={selectedId === m.messageId ? 'true' : undefined}
              className="block w-full truncate text-left text-xs text-slate-600"
            >
              {m.subject}
              {m.hasAttachments && (
                <span className="ml-1" role="img" aria-label={t('email_hasAttachments')}>
                  📎
                </span>
              )}
            </button>
          </div>
        </div>
      ))}
    </Card>
  );
}
