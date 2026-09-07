import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import clsx from 'clsx';
import { emailIntegrationApi, type EmailPrefillDto, type InboxMessageSummaryDto } from '../../api/emailIntegration';
import { Button, Card, Spinner } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { SafeHtml } from '../../components/SafeHtml';
import { format, formatDistanceToNow } from 'date-fns';
import { useAuthStore } from '../../store/authStore';

function buildMicrosoftAuthUrl(clientId: string, redirectUri: string, state: string) {
  const params = new URLSearchParams({
    client_id: clientId,
    response_type: 'code',
    redirect_uri: redirectUri,
    response_mode: 'query',
    scope: 'offline_access Mail.Read User.Read',
    state,
    // Force Microsoft's account picker every time — without this it silently reuses whatever
    // Microsoft account already has an active browser session, which isn't necessarily the
    // branch mailbox account the person meant to sign in with.
    prompt: 'select_account',
  });
  return `https://login.microsoftonline.com/common/oauth2/v2.0/authorize?${params.toString()}`;
}

export default function EmailsPage() {
  const user = useAuthStore((s) => s.user);
  const [connected, setConnected] = useState<boolean | null>(null);
  const [connectedEmail, setConnectedEmail] = useState<string | null>(null);
  const [messages, setMessages] = useState<InboxMessageSummaryDto[]>([]);
  const [tab, setTab] = useState<'inbox' | 'marked'>('inbox');
  const [loading, setLoading] = useState(true);
  const [connecting, setConnecting] = useState(false);
  const [disconnecting, setDisconnecting] = useState(false);
  const [confirmDisconnect, setConfirmDisconnect] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<EmailPrefillDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  function refreshMessages() {
    emailIntegrationApi
      .listMessages(30)
      .then(setMessages)
      .catch(() => setError('Could not load your inbox. Try reconnecting your mailbox.'));
  }

  useEffect(() => {
    emailIntegrationApi
      .getStatus()
      .then((s) => {
        setConnected(s.connected);
        setConnectedEmail(s.connectedEmail);
        if (s.connected) refreshMessages();
      })
      .finally(() => setLoading(false));
  }, []);

  async function handleConnect() {
    setConnecting(true);
    const { clientId } = await emailIntegrationApi.getConfig();
    if (!clientId) {
      setError('Outlook integration is not configured yet (missing Client ID). Ask an admin to finish setup.');
      setConnecting(false);
      return;
    }
    const redirectUri = `${window.location.origin}/emails/callback`;
    const state = Math.random().toString(36).slice(2);
    sessionStorage.setItem('ms_oauth_state', state);
    window.location.href = buildMicrosoftAuthUrl(clientId, redirectUri, state);
  }

  async function handleDisconnectConfirmed() {
    setConfirmDisconnect(false);
    setDisconnecting(true);
    try {
      await emailIntegrationApi.disconnect();
      setConnected(false);
      setConnectedEmail(null);
      setMessages([]);
      setSelectedId(null);
      setDetail(null);
    } finally {
      setDisconnecting(false);
    }
  }

  async function handleToggleMark(m: InboxMessageSummaryDto) {
    const marked = await emailIntegrationApi.setMarked(m.messageId, !m.isMarked);
    setMessages((prev) => prev.map((x) => (x.messageId === m.messageId ? { ...x, isMarked: marked } : x)));
  }

  function selectMessage(id: string) {
    setSelectedId(id);
    setDetail(null);
    setDetailLoading(true);
    emailIntegrationApi
      .getPrefill(id)
      .then(setDetail)
      .catch(() => setError('Could not load this email.'))
      .finally(() => setDetailLoading(false));
  }

  if (loading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }

  if (!user?.departmentId) {
    return (
      <div className="mx-auto max-w-lg">
        <h1 className="mb-2 text-xl font-bold text-slate-900">Emails</h1>
        <Card className="p-6 text-center">
          <p className="text-sm text-slate-600">
            You're not assigned to a branch yet. Ask an admin to assign you to one before connecting a mailbox.
          </p>
        </Card>
      </div>
    );
  }

  if (!connected) {
    return (
      <div className="mx-auto max-w-lg">
        <h1 className="mb-2 text-xl font-bold text-slate-900">Emails</h1>
        <Card className="p-6 text-center">
          <p className="mb-4 text-sm text-slate-600">
            Connect your branch's shared mailbox ({user.departmentName}) to see incoming emails here and turn them
            into tickets. Everyone in your branch will see the same inbox and marks — sign in with the branch's own
            mailbox account, not your personal one.
          </p>
          {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
          <Button onClick={handleConnect} disabled={connecting}>
            {connecting ? 'Redirecting…' : '🔗 Connect branch mailbox'}
          </Button>
        </Card>
      </div>
    );
  }

  const visible = tab === 'inbox' ? messages : messages.filter((m) => m.isMarked);
  const selected = visible.find((m) => m.messageId === selectedId) ?? null;

  return (
    <div className="mx-auto max-w-6xl space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Emails</h1>
          <p className="text-xs text-slate-400">Connected: {connectedEmail}</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={refreshMessages}>
            Refresh
          </Button>
          <Button variant="secondary" className="text-red-600" disabled={disconnecting} onClick={() => setConfirmDisconnect(true)}>
            {disconnecting ? 'Disconnecting…' : 'Disconnect'}
          </Button>
        </div>
      </div>

      {error && <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <div className="flex gap-2 border-b border-slate-200">
        <button
          onClick={() => setTab('inbox')}
          className={`px-3 py-2 text-sm font-medium ${tab === 'inbox' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          Inbox
        </button>
        <button
          onClick={() => setTab('marked')}
          className={`px-3 py-2 text-sm font-medium ${tab === 'marked' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          Marked for tickets ({messages.filter((m) => m.isMarked).length})
        </button>
      </div>

      <div className="flex gap-4">
        <Card className="w-96 shrink-0 divide-y divide-slate-100 overflow-y-auto" style={{ maxHeight: '32rem' }}>
          {visible.length === 0 && (
            <p className="px-4 py-8 text-center text-sm text-slate-400">
              {tab === 'inbox' ? 'No emails found.' : 'No emails marked as tickets yet.'}
            </p>
          )}
          {visible.map((m) => (
            <div
              key={m.messageId}
              onClick={() => selectMessage(m.messageId)}
              className={clsx(
                'flex cursor-pointer items-start gap-2 px-3 py-2 hover:bg-slate-50',
                selectedId === m.messageId && 'bg-blue-50 hover:bg-blue-50'
              )}
            >
              <input
                type="checkbox"
                checked={m.isMarked}
                onClick={(e) => e.stopPropagation()}
                onChange={() => handleToggleMark(m)}
                className="mt-1 h-3.5 w-3.5 shrink-0"
                title="Mark as ticket"
              />
              <div className="min-w-0 flex-1">
                <div className="flex items-center justify-between gap-2">
                  <span className="truncate text-xs font-semibold text-slate-800">{m.fromName || m.fromEmail}</span>
                  <span className="shrink-0 text-[11px] text-slate-400">
                    {formatDistanceToNow(new Date(m.receivedAtUtc), { addSuffix: true })}
                  </span>
                </div>
                <div className="truncate text-xs text-slate-600">
                  {m.subject}
                  {m.hasAttachments && <span className="ml-1">📎</span>}
                </div>
              </div>
            </div>
          ))}
        </Card>

        <Card className="flex-1 p-5">
          {!selected ? (
            <p className="py-16 text-center text-sm text-slate-400">Select an email to read it.</p>
          ) : detailLoading || !detail ? (
            <div className="flex justify-center py-16">
              <Spinner className="h-6 w-6 text-blue-700" />
            </div>
          ) : (
            <div>
              <div className="mb-3 flex items-start justify-between gap-3 border-b border-slate-100 pb-3">
                <div>
                  <h2 className="text-base font-bold text-slate-900">{detail.subject}</h2>
                  <p className="mt-1 text-xs text-slate-500">
                    From {selected.fromName ? `${selected.fromName} <${detail.clientEmail}>` : detail.clientEmail} ·{' '}
                    {format(new Date(detail.receivedAtUtc), 'dd.MM.yyyy HH:mm')}
                  </p>
                </div>
                {selected.isMarked && (
                  <div className="shrink-0">
                    {selected.convertedTicketId ? (
                      <Link to={`/tickets/${selected.convertedTicketId}`} className="text-xs font-medium text-green-700 hover:underline">
                        View ticket →
                      </Link>
                    ) : (
                      <Link
                        to={`/tickets/new-on-behalf?fromEmail=${encodeURIComponent(selected.messageId)}`}
                        className="text-xs font-medium text-blue-700 hover:underline"
                      >
                        Open ticket →
                      </Link>
                    )}
                  </div>
                )}
              </div>
              <SafeHtml className="prose prose-sm max-w-none" html={detail.bodyHtml} />
            </div>
          )}
        </Card>
      </div>

      {confirmDisconnect && (
        <ConfirmDialog
          title="Disconnect the branch mailbox?"
          message="Everyone in your branch will lose access to this inbox until someone reconnects it."
          confirmLabel="Disconnect"
          onConfirm={handleDisconnectConfirmed}
          onCancel={() => setConfirmDisconnect(false)}
        />
      )}
    </div>
  );
}
