import { useState } from 'react';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { INBOX_SIZE, useInbox } from '../../api/emailQueries';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Tabs } from '../../components/Tabs';
import { Button, ErrorText } from '../../components/ui';
import type { InboxMessageSummaryDto } from '../../api/emailIntegration';
import { MessageDetail } from './MessageDetail';
import { MessageList } from './MessageList';
import { useLanguage } from '../../i18n/LanguageContext';

type InboxTab = 'inbox' | 'marked';

/** The connected mailbox: who it is, the latest messages in two tabs (all, and the ones marked for tickets), the message that is chosen, and the way to disconnect. */
export function InboxView({ connectedEmail }: { connectedEmail: string | null }) {
  const { t } = useLanguage();
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
  const emptyMessage = inbox.isPending ? t('email_loadingEmails') : inbox.isError ? null : tab === 'inbox' ? t('email_noEmails') : t('email_noMarked');

  async function handleToggleMark(m: InboxMessageSummaryDto) {
    setActionError(null);
    try {
      await emailIntegrationApi.setMarked(m.messageId, !m.isMarked);
    } catch {
      setActionError(t('email_markFail'));
    }
  }

  async function handleDisconnect() {
    setConfirmDisconnect(false);
    setDisconnecting(true);
    setActionError(null);
    try {
      await emailIntegrationApi.disconnect();
    } catch {
      setActionError(t('email_disconnectFail'));
    } finally {
      setDisconnecting(false);
    }
  }

  return (
    <div className="mx-auto max-w-6xl space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{t('nav_emails')}</h1>
          <p className="text-xs text-slate-600">Connected: {connectedEmail}</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" disabled={inbox.isFetching} onClick={() => void inbox.refetch()}>
            {inbox.isRefetching ? 'Refreshing…' : 'Refresh'}
          </Button>
          <Button variant="secondary" className="text-red-600" disabled={disconnecting} onClick={() => setConfirmDisconnect(true)}>
            {disconnecting ? t('email_disconnecting') : t('email_disconnect')}
          </Button>
        </div>
      </div>

      {inbox.isError && <ErrorText>{t('email_inboxLoadFail')}</ErrorText>}
      <ErrorText>{actionError}</ErrorText>

      <Tabs
        label={t('email_mailboxLabel')}
        tabs={[
          { key: 'inbox', label: t('email_inbox') },
          { key: 'marked', label: t('email_marked', { count: marked.length }) },
        ]}
        active={tab}
        onSelect={setTab}
      />
      {messages.length >= INBOX_SIZE && <p className="text-xs text-slate-600">{t('email_showingLatest', { count: INBOX_SIZE })}</p>}

      <div className="flex gap-4">
        <MessageList messages={visible} message={emptyMessage} selectedId={selectedId} onSelect={setSelectedId} onToggleMark={handleToggleMark} />
        <MessageDetail selected={selected} />
      </div>

      {confirmDisconnect && (
        <ConfirmDialog
          title={t('email_disconnectTitle')}
          message={t('email_disconnectMsg')}
          confirmLabel={t('email_disconnect')}
          onConfirm={handleDisconnect}
          onCancel={() => setConfirmDisconnect(false)}
        />
      )}
    </div>
  );
}
