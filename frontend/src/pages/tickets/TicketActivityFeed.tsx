import { useEffect, useState } from 'react';
import { format } from 'date-fns';
import { ticketsApi } from '../../api/tickets';
import type { MessageType, TicketDetailDto, TicketMessageDto } from '../../types';
import { Button, ErrorText } from '../../components/ui';
import { RichTextEditor } from '../../components/RichTextEditor';
import { SafeHtml } from '../../components/SafeHtml';
import { sanitizeHtml } from '../../lib/sanitizeHtml';
import { newIdempotencyKey } from '../../lib/idempotency';
import { clearDraft, draftKey, loadDraft, saveDraft } from '../../lib/drafts';
import { isBlankHtml } from '../../lib/htmlText';
import { useLanguage } from '../../i18n/LanguageContext';
import { translateEventHtml } from '../../i18n/serverMessages';

/// Unified chat-like feed: Response, Internal Note, and system lifecycle events (opened/assigned/transferred/closed)
/// all shown together in one chronological stream, distinguished by bubble color — no more switching tabs to see everything.
export function TicketActivityFeed({
  ticket,
  messages,
  canReplyResponse,
  canReplyInternal,
  onMessageAdded,
}: {
  ticket: TicketDetailDto;
  messages: TicketMessageDto[];
  canReplyResponse: boolean;
  canReplyInternal: boolean;
  onMessageAdded: (m: TicketMessageDto) => void;
}) {
  const { t, language } = useLanguage();
  const [replyType, setReplyType] = useState<MessageType>('Response');
  // Attachments belong to the mode they were added in, exactly like the text: a file added to an internal note (a screenshot of
  // an internal system, say) must not travel with the public reply when the person switches modes.
  const [filesByType, setFilesByType] = useState<Partial<Record<MessageType, File[]>>>({});
  const [sending, setSending] = useState(false);
  const [sendError, setSendError] = useState<string | null>(null);

  const canReplyAtAll = canReplyResponse || canReplyInternal;
  const effectiveType: MessageType = canReplyResponse && canReplyInternal ? replyType : canReplyInternal ? 'InternalNote' : 'Response';

  // What is being written is kept in the tab while it is not sent: if the sign-in expires in the middle of a reply, the person is
  // taken to the sign-in page and the words are still here when they come back.
  const storedUnder = draftKey(ticket.id, effectiveType);
  const [body, setBody] = useState(() => loadDraft(storedUnder));
  useEffect(() => {
    setBody(loadDraft(storedUnder));
  }, [storedUnder]);
  useEffect(() => {
    saveDraft(storedUnder, body);
  }, [storedUnder, body]);

  // One key per message being composed: sending the same text again (a double click, a retry after a timeout) is the same message,
  // not two. Changing the text, or sending it, makes it a different message and so a new key.
  const [messageKey, setMessageKey] = useState(newIdempotencyKey);
  useEffect(() => {
    setMessageKey(newIdempotencyKey());
  }, [body, effectiveType]);

  const files = filesByType[effectiveType] ?? [];

  function addFiles(newFiles: File[]) {
    setFilesByType((prev) => ({ ...prev, [effectiveType]: [...(prev[effectiveType] ?? []), ...newFiles] }));
  }

  function removeFile(index: number) {
    setFilesByType((prev) => ({ ...prev, [effectiveType]: (prev[effectiveType] ?? []).filter((_, i) => i !== index) }));
  }

  async function handleSend() {
    if (isBlankHtml(body)) return;
    setSending(true);
    setSendError(null);
    try {
      const message = (await ticketsApi.addMessage(ticket.id, effectiveType, body, messageKey)) as TicketMessageDto;
      if (files.length > 0) {
        const uploaded = await ticketsApi.uploadAttachments(ticket.id, files, message.id);
        message.attachments = uploaded;
      }
      onMessageAdded(message);
      clearDraft(storedUnder);
      setBody('');
      setFilesByType((prev) => ({ ...prev, [effectiveType]: [] }));
    } catch (err: any) {
      // The text stays in the editor (and in the draft); pressing send again uses the same key, so it cannot be posted twice.
      setSendError(err?.response?.data?.message ?? t('feed_sendError'));
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-3">
        {messages.length === 0 && <p className="text-sm text-slate-600">{t('feed_noActivity')}</p>}
        {messages.map((m) => {
          if (m.type === 'SystemEvent') {
            return (
              <div key={m.id} className="flex items-center justify-center gap-2 py-1 text-center text-xs text-slate-600">
                <span className="h-px flex-1 bg-slate-100" />
                <span className="whitespace-nowrap" dangerouslySetInnerHTML={{ __html: sanitizeHtml(translateEventHtml(m.bodyHtml, language)) }} />
                <span>· {format(new Date(m.createdAt), 'dd.MM.yyyy HH:mm')}</span>
                <span className="h-px flex-1 bg-slate-100" />
              </div>
            );
          }

          const isInternal = m.type === 'InternalNote';
          return (
            <div
              key={m.id}
              className={`rounded-lg border p-4 ${isInternal ? 'border-amber-200 bg-amber-50' : 'border-blue-100 bg-blue-50'}`}
            >
              <div className="mb-2 flex items-center justify-between text-xs text-slate-600">
                <span className="flex items-center gap-2">
                  <span className="font-semibold text-slate-700">{m.authorName}</span>
                  <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase ${isInternal ? 'bg-amber-200 text-amber-800' : 'bg-blue-200 text-blue-800'}`}>
                    {isInternal ? t('feed_internalNote') : t('feed_response')}
                  </span>
                </span>
                <span>{format(new Date(m.createdAt), 'dd.MM.yyyy HH:mm')}</span>
              </div>
              <SafeHtml className="prose prose-sm max-w-none" html={m.bodyHtml} />
              {m.attachments.length > 0 && (
                <ul className="mt-2 space-y-1 text-xs">
                  {m.attachments.map((a) => (
                    <li key={a.id}>
                      <a href={a.fileUrl} target="_blank" rel="noreferrer" className="text-blue-700 hover:underline">
                        <span aria-hidden>📎</span> {a.fileName}
                      </a>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          );
        })}
      </div>

      {canReplyAtAll && (
        <div className="space-y-2">
          {canReplyResponse && canReplyInternal && (
            <div className="flex gap-2 text-xs">
              <button
                type="button"
                aria-pressed={replyType === 'Response'}
                onClick={() => setReplyType('Response')}
                className={`rounded-full px-3 py-1 font-semibold ${replyType === 'Response' ? 'bg-blue-700 text-white' : 'bg-slate-100 text-slate-600'}`}
              >
                {t('feed_response')}
              </button>
              <button
                type="button"
                aria-pressed={replyType === 'InternalNote'}
                onClick={() => setReplyType('InternalNote')}
                className={`rounded-full px-3 py-1 font-semibold ${replyType === 'InternalNote' ? 'bg-amber-500 text-white' : 'bg-slate-100 text-slate-600'}`}
              >
                {t('feed_internalNote')}
              </button>
            </div>
          )}
          <RichTextEditor
            value={body}
            onChange={setBody}
            onFilesSelected={addFiles}
            ariaLabel={effectiveType === 'Response' ? t('feed_replyAria') : t('feed_internalAria')}
            placeholder={effectiveType === 'Response' ? t('feed_replyPlaceholder') : t('feed_internalPlaceholder')}
          />
          {files.length > 0 && (
            <ul className="flex flex-wrap gap-2">
              {files.map((f, i) => (
                <li key={`${f.name}-${i}`} className="flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-600">
                  <span aria-hidden>📎</span> {f.name}
                  <button type="button" aria-label={t('feed_removeFile', { name: f.name })} onClick={() => removeFile(i)} className="ml-1 font-bold text-slate-600 hover:text-red-600">
                    ×
                  </button>
                </li>
              ))}
            </ul>
          )}
          <ErrorText>{sendError}</ErrorText>
          <Button onClick={handleSend} disabled={sending}>
            {sending ? t('common_sending') : effectiveType === 'Response' ? t('feed_sendReply') : t('feed_addInternalNote')}
          </Button>
        </div>
      )}
    </div>
  );
}
