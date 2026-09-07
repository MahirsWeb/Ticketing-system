import { useState } from 'react';
import { format } from 'date-fns';
import { ticketsApi } from '../../api/tickets';
import type { MessageType, TicketDetailDto, TicketMessageDto } from '../../types';
import { Button } from '../../components/ui';
import { RichTextEditor } from '../../components/RichTextEditor';
import { SafeHtml } from '../../components/SafeHtml';

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
  const [body, setBody] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [replyType, setReplyType] = useState<MessageType>('Response');
  const [sending, setSending] = useState(false);

  const canReplyAtAll = canReplyResponse || canReplyInternal;
  const effectiveType: MessageType = canReplyResponse && canReplyInternal ? replyType : canReplyInternal ? 'InternalNote' : 'Response';

  function addFiles(newFiles: File[]) {
    setFiles((prev) => [...prev, ...newFiles]);
  }

  function removeFile(index: number) {
    setFiles((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleSend() {
    if (!body.trim() || body === '<p></p>') return;
    setSending(true);
    try {
      const message = (await ticketsApi.addMessage(ticket.id, effectiveType, body)) as TicketMessageDto;
      if (files.length > 0) {
        const uploaded = await ticketsApi.uploadAttachments(ticket.id, files, message.id);
        message.attachments = uploaded;
      }
      onMessageAdded(message);
      setBody('');
      setFiles([]);
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-3">
        {messages.length === 0 && <p className="text-sm text-slate-400">No activity yet.</p>}
        {messages.map((m) => {
          if (m.type === 'SystemEvent') {
            return (
              <div key={m.id} className="flex items-center justify-center gap-2 py-1 text-center text-xs text-slate-400">
                <span className="h-px flex-1 bg-slate-100" />
                <span className="whitespace-nowrap" dangerouslySetInnerHTML={{ __html: m.bodyHtml }} />
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
                        📎 {a.fileName}
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
                  📎 {f.name}
                  <button type="button" onClick={() => removeFile(i)} className="ml-1 font-bold text-slate-400 hover:text-red-600">
                    ×
                  </button>
                </li>
              ))}
            </ul>
          )}
          <Button onClick={handleSend} disabled={sending}>
            {sending ? 'Sending…' : effectiveType === 'Response' ? 'Send reply' : 'Add internal note'}
          </Button>
        </div>
      )}
    </div>
  );
}
