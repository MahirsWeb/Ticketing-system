import { useState } from 'react';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { INBOX_SIZE, useInbox } from '../../api/emailQueries';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Tabs } from '../../components/Tabs';
import { Button, ErrorText } from '../../components/ui';
import type { InboxMessageSummaryDto } from '../../api/emailIntegration';
import { MessageDetail } from './MessageDetail';
import { MessageList } from './MessageList';

type InboxTab = 'inbox' | 'marked';

/** The connected mailbox: who it is, the latest messages in two tabs (all, and the ones marked for tickets), the message that is chosen, and the way to disconnect. */
export function InboxView({ connectedEmail }: { connectedEmail: string | null }) {
  const inbox = useInbox();
  const [tab, setTab] = useState<InboxTab>('inbox');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [confirmDisconnect, setConfirmDisconnect] = useState(false);
  const [disconnecting, setDisconnecting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const messages = inbox.data ?? [];
  const marked = messages.filter((m) => m.isMarked);
  const visible = tab === 'inbox' ? messages : marked;
  const selected = visible.find((m) => m.messageId === selectedId) ?? null;
  // What the empty list says depends on why it is empty: not here yet, could not be had, or nothing in this tab.
  const emptyMessage = inbox.isPending ? 'Loading emails…' : inbox.isError ? null : tab === 'inbox' ? 'No emails found.' : 'No emails marked as tickets yet.';

  async function handleToggleMark(m: InboxMessageSummaryDto) {
    setActionError(null);
    try {
      await emailIntegrationApi.setMarked(m.messageId, !m.isMarked);
    } catch {
      setActionError('Could not change the mark. Try again.');
    }
  }

  async function handleDisconnect() {
    setConfirmDisconnect(false);
    setDisconnecting(true);
    setActionError(null);
    try {
      await emailIntegrationApi.disconnect();
    } catch {
      setActionError('Could not disconnect the mailbox. Try again.');
    } finally {
      setDisconnecting(false);
    }
  }

  return (
    <div className="mx-auto max-w-6xl space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Emails</h1>
          <p className="text-xs text-slate-600">Connected: {connectedEmail}</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" disabled={inbox.isFetching} onClick={() => void inbox.refetch()}>
            {inbox.isRefetching ? 'Refreshing…' : 'Refresh'}
          </Button>
          <Button variant="secondary" className="text-red-600" disabled={disconnecting} onClick={() => setConfirmDisconnect(true)}>
            {disconnecting ? 'Disconnecting…' : 'Disconnect'}
          </Button>
        </div>
      </div>

      {inbox.isError && <ErrorText>Could not load your inbox. Try reconnecting your mailbox.</ErrorText>}
      <ErrorText>{actionError}</ErrorText>

      <Tabs
        label="Mailbox"
        tabs={[
          { key: 'inbox', label: 'Inbox' },
          { key: 'marked', label: `Marked for tickets (${marked.length})` },
        ]}
        active={tab}
        onSelect={setTab}
      />
      {messages.length >= INBOX_SIZE && <p className="text-xs text-slate-600">Showing the latest {INBOX_SIZE} emails.</p>}

      <div className="flex gap-4">
        <MessageList messages={visible} message={emptyMessage} selectedId={selectedId} onSelect={setSelectedId} onToggleMark={handleToggleMark} />
        <MessageDetail selected={selected} />
      </div>

      {confirmDisconnect && (
        <ConfirmDialog
          title="Disconnect the branch mailbox?"
          message="Everyone in your branch will lose access to this inbox until someone reconnects it."
          confirmLabel="Disconnect"
          onConfirm={handleDisconnect}
          onCancel={() => setConfirmDisconnect(false)}
        />
      )}
    </div>
  );
}
