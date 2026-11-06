import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { format } from 'date-fns';
import { ticketsApi } from '../../api/tickets';
import { ticketKeys, useTicket } from '../../api/ticketQueries';
import type { TicketDetailDto } from '../../types';
import { Card, ErrorText, PriorityBadge, Spinner, StatusBadge } from '../../components/ui';
import { SafeHtml } from '../../components/SafeHtml';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { useAuthStore } from '../../store/authStore';
import { OpenTicketForm } from './OpenTicketForm';
import { CloseTicketForm } from './CloseTicketForm';
import { TransferBranchForm } from './TransferBranchForm';
import { TicketStatusActions } from './TicketStatusActions';
import { TicketActivityFeed } from './TicketActivityFeed';
import { TicketAssigneesPanel } from './TicketAssigneesPanel';
import { WorkTimeEditor } from './WorkTimeEditor';
import { ClientInfoPopover } from '../../components/ClientInfoPopover';
import { KnowledgeBaseSearchPanel } from './KnowledgeBaseSearchPanel';
import { ClientAiHelpPanel } from './ClientAiHelpPanel';

function InfoRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{label}</div>
      <div className="text-sm text-slate-800">{value ?? '—'}</div>
    </div>
  );
}

export default function TicketDetailPage() {
  const { id } = useParams<{ id: string }>();
  const user = useAuthStore((s) => s.user);
  const queryClient = useQueryClient();
  // Another ticket is another entry: the previous one is forgotten at once (its drafts and its data must not be shown under the new
  // address) and a slow answer for a ticket that is no longer the one on screen has nowhere to land.
  const { data: ticket, isPending: loading, error } = useTicket(id);
  const loadError = error ? ((error as { response?: { data?: { message?: string } } }).response?.data?.message ?? 'Could not load this ticket.') : null;
  /** Every change comes back as the whole ticket; it is written into the entry, or worked out from the one that is there now. */
  function setTicket(next: TicketDetailDto | ((current: TicketDetailDto | undefined) => TicketDetailDto | undefined)) {
    if (id) queryClient.setQueryData<TicketDetailDto>(ticketKeys.detail(id), next);
  }
  const [confirmReopen, setConfirmReopen] = useState(false);
  const [reopening, setReopening] = useState(false);

  async function handleReopenConfirmed() {
    if (!id) return;
    setConfirmReopen(false);
    setReopening(true);
    try {
      setTicket(await ticketsApi.reopen(id));
    } finally {
      setReopening(false);
    }
  }

  if (loading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }
  if (loadError) return <ErrorText>{loadError}</ErrorText>;
  if (!ticket || !user) return <p>Ticket not found.</p>;

  const isStaff = user.role === 'Admin' || user.role === 'Employee';
  const canOpen = isStaff && ticket.status === 'New';
  const canClose = isStaff && ticket.status !== 'New' && ticket.status !== 'Closed';
  const canReopen = isStaff && ticket.status === 'Closed';
  const canTransfer = isStaff && ticket.status !== 'New' && ticket.status !== 'Closed';
  const canSeeInternalNotes = user.role !== 'Client';
  const canReplyResponse = ticket.status !== 'Closed';
  const canReplyInternal = ticket.status !== 'Closed' && canSeeInternalNotes;

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-xl font-bold text-slate-900">
            #{ticket.ticketNumber} — {ticket.title}
          </h1>
          <StatusBadge status={ticket.status} />
          {isStaff && <PriorityBadge priority={ticket.priority} />}
        </div>
        <p className="mt-1 text-sm text-slate-600">
          Submitted by{' '}
          <ClientInfoPopover
            name={ticket.clientName}
            email={ticket.clientEmail}
            phoneNumber={ticket.clientPhoneNumber}
            companyName={ticket.companyName}
          />{' '}
          ({ticket.companyName}) on {format(new Date(ticket.createdAt), 'dd.MM.yyyy HH:mm')}
        </p>
        {isStaff && (
          <div className="mt-3">
            <TicketStatusActions key={`status-${ticket.id}`} ticket={ticket} onChanged={setTicket} />
          </div>
        )}
      </div>

      {ticket.status !== 'New' && (
        <Card className="p-5">
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            <InfoRow label="Source" value={ticket.source} />
            <InfoRow label="Help topic" value={ticket.helpTopicName} />
            <InfoRow label="Branch" value={ticket.departmentName} />
            <InfoRow label="Sub-branch" value={ticket.subBranchName} />
            {isStaff && <InfoRow label="Due date" value={ticket.dueDateUtc ? format(new Date(ticket.dueDateUtc), 'dd.MM.yyyy HH:mm') : null} />}
            <InfoRow label="Opened at" value={ticket.openedAtUtc ? format(new Date(ticket.openedAtUtc), 'dd.MM.yyyy HH:mm') : null} />
            <InfoRow label="Closed at" value={ticket.closedAtUtc ? format(new Date(ticket.closedAtUtc), 'dd.MM.yyyy HH:mm') : null} />
            {isStaff && <InfoRow label="SLA plan" value={ticket.slaPlanName} />}
            {isStaff && <InfoRow label="Category" value={ticket.categoryName} />}
          </div>
          {isStaff && (
            <div className="mt-4 space-y-4 border-t border-slate-100 pt-4">
              <TicketAssigneesPanel key={`assignees-${ticket.id}`} ticket={ticket} onChanged={setTicket} />
              <WorkTimeEditor key={`worktime-${ticket.id}`} ticket={ticket} onChanged={setTicket} />
            </div>
          )}
          {canTransfer && (
            <div className="mt-4 border-t border-slate-100 pt-3">
              <TransferBranchForm key={`transfer-${ticket.id}`} ticket={ticket} onTransferred={setTicket} />
            </div>
          )}
        </Card>
      )}

      <Card className="p-5">
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">Original request</h2>
        <SafeHtml className="prose prose-sm max-w-none" html={ticket.description} />
        {ticket.attachments.length > 0 && (
          <ul className="mt-3 space-y-1 text-xs">
            {ticket.attachments.map((a) => (
              <li key={a.id}>
                <a href={a.fileUrl} target="_blank" rel="noreferrer" className="text-blue-700 hover:underline">
                  <span aria-hidden>📎</span> {a.fileName}
                </a>
              </li>
            ))}
          </ul>
        )}
      </Card>

      {ticket.status === 'Closed' && (
        <Card className="p-5">
          <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">Resolution</h2>
          <p className="whitespace-pre-wrap text-sm text-slate-800">{ticket.resolutionSummary}</p>
          {canReopen && (
            <button
              type="button"
              disabled={reopening}
              onClick={() => setConfirmReopen(true)}
              className="mt-3 text-xs text-slate-600 hover:text-slate-800 hover:underline disabled:opacity-50"
            >
              {reopening ? 'Reopening…' : 'Reopen ticket…'}
            </button>
          )}
        </Card>
      )}

      {canSeeInternalNotes && <KnowledgeBaseSearchPanel key={`knowledge-${ticket.id}`} initialQuery={ticket.title} />}
      {!canSeeInternalNotes && <ClientAiHelpPanel key={`ai-help-${ticket.id}`} initialQuery={ticket.title} />}

      {canOpen && <OpenTicketForm key={`open-${ticket.id}`} ticket={ticket} onOpened={setTicket} />}

      <Card className="p-5">
        <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">Activity</h2>
        <TicketActivityFeed key={`activity-${ticket.id}`}
          ticket={ticket}
          messages={ticket.messages}
          canReplyResponse={canReplyResponse}
          canReplyInternal={canReplyInternal}
          // Built from the ticket as it is when the reply ARRIVES, not as it was when the reply box was drawn: the agent may have
          // pressed "Start work" or changed the assignees in the meantime, and that must stay.
          onMessageAdded={(m) => setTicket((current) => (current && current.id === ticket.id ? { ...current, messages: [...current.messages, m] } : current))}
        />
      </Card>

      {canClose && <CloseTicketForm key={`close-${ticket.id}`} ticket={ticket} onClosed={setTicket} />}

      {confirmReopen && (
        <ConfirmDialog
          title="Reopen this ticket?"
          message="The ticket goes back to Open and the client and assignees can pick it back up. Its previous resolution notes stay in place until it's closed again."
          confirmLabel="Reopen"
          onConfirm={handleReopenConfirmed}
          onCancel={() => setConfirmReopen(false)}
        />
      )}
    </div>
  );
}
