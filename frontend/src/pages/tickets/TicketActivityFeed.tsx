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
  const [replyType, setReplyType] = useState<MessageType>('Response');
  const [files, setFiles] = useState<File[]>([]);
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

  function addFiles(newFiles: File[]) {
    setFiles((prev) => [...prev, ...newFiles]);
  }

  function removeFile(index: number) {
    setFiles((prev) => prev.filter((_, i) => i !== index));
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
      setFiles([]);
    } catch (err: any) {
      // The text stays in the editor (and in the draft); pressing send again uses the same key, so it cannot be posted twice.
      setSendError(err?.response?.data?.message ?? 'The message could not be sent. Your text is still here; try again.');
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-3">
        {messages.length === 0 && <p className="text-sm text-slate-500">No activity yet.</p>}
        {messages.map((m) => {
          if (m.type === 'SystemEvent') {
            return (
              <div key={m.id} className="flex items-center justify-center gap-2 py-1 text-center text-xs text-slate-500">
                <span className="h-px flex-1 bg-slate-100" />
                <span className="whitespace-nowrap" dangerouslySetInnerHTML={{ __html: sanitizeHtml(m.bodyHtml) }} />
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
              <div className="mb-2 flex items-center justify-between text-xs text-slate-500">
                <span className="flex items-center gap-2">
                  <span className="font-semibold text-slate-700">{m.authorName}</span>
                  <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase ${isInternal ? 'bg-amber-200 text-amber-800' : 'bg-blue-200 text-blue-800'}`}>
                    {isInternal ? 'Internal note' : 'Response'}
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
                onClick={() => setReplyType('Response')}
                className={`rounded-full px-3 py-1 font-semibold ${replyType === 'Response' ? 'bg-blue-700 text-white' : 'bg-slate-100 text-slate-600'}`}
              >
                Response
              </button>
              <button
                type="button"
                onClick={() => setReplyType('InternalNote')}
                className={`rounded-full px-3 py-1 font-semibold ${replyType === 'InternalNote' ? 'bg-amber-500 text-white' : 'bg-slate-100 text-slate-600'}`}
              >
                Internal note
              </button>
            </div>
          )}
          <RichTextEditor
            value={body}
            onChange={setBody}
            onFilesSelected={addFiles}
            placeholder={effectiveType === 'Response' ? 'Write a reply…' : 'Write an internal note (not visible to the client)…'}
          />
          {files.length > 0 && (
            <ul className="flex flex-wrap gap-2">
              {files.map((f, i) => (
                <li key={`${f.name}-${i}`} className="flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-600">
                  <span aria-hidden>📎</span> {f.name}
                  <button type="button" onClick={() => removeFile(i)} className="ml-1 font-bold text-slate-500 hover:text-red-600">
                    ×
                  </button>
                </li>
              ))}
            </ul>
          )}
          <ErrorText>{sendError}</ErrorText>
          <Button onClick={handleSend} disabled={sending}>
            {sending ? 'Sending…' : effectiveType === 'Response' ? 'Send reply' : 'Add internal note'}
          </Button>
        </div>
      )}
    </div>
  );
}
