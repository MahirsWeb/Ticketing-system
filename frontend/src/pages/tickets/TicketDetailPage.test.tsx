import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TicketDetailPage from './TicketDetailPage';
import { ticketsApi } from '../../api/tickets';
import { useAuthStore } from '../../store/authStore';
import type { TicketDetailDto, TicketMessageDto, UserSummary } from '../../types';

vi.mock('../../api/tickets', () => ({ ticketsApi: { getById: vi.fn() } }));

// The parts of the page that are not the subject here are stood in for by nothing.
vi.mock('./OpenTicketForm', () => ({ OpenTicketForm: () => null }));
vi.mock('./CloseTicketForm', () => ({ CloseTicketForm: () => null }));
vi.mock('./TransferBranchForm', () => ({ TransferBranchForm: () => null }));
vi.mock('./TicketAssigneesPanel', () => ({ TicketAssigneesPanel: () => null }));
vi.mock('./WorkTimeEditor', () => ({ WorkTimeEditor: () => null }));
vi.mock('./KnowledgeBaseSearchPanel', () => ({ KnowledgeBaseSearchPanel: () => null }));
vi.mock('./ClientAiHelpPanel', () => ({ ClientAiHelpPanel: () => null }));

// The status buttons and the reply box are stood in for by two buttons that do what the real ones do to the page:
// the first reports a changed ticket; the second hands in a message through the callback the page gave the reply box when it was drawn FIRST.
const firstDrawn: { onMessageAdded?: (m: TicketMessageDto) => void }[] = [];
vi.mock('./TicketStatusActions', () => ({
  TicketStatusActions: ({ ticket, onChanged }: { ticket: TicketDetailDto; onChanged: (t: TicketDetailDto) => void }) => (
    <button onClick={() => onChanged({ ...ticket, status: 'InProgress' })}>start work</button>
  ),
}));
vi.mock('./TicketActivityFeed', () => ({
  TicketActivityFeed: ({ ticket, messages, onMessageAdded }: { ticket: TicketDetailDto; messages: unknown[]; onMessageAdded: (m: TicketMessageDto) => void }) => {
    if (firstDrawn.length === 0) firstDrawn.push({ onMessageAdded });
    return (
      <div>
        <p data-testid="feed">{`${ticket.status}|${messages.length}`}</p>
        <button onClick={() => firstDrawn[0].onMessageAdded!({ id: 'm-1' } as TicketMessageDto)}>late reply</button>
      </div>
    );
  },
}));

const employee = { id: 'u1', firstName: 'Amra', lastName: 'H', role: 'Employee' } as UserSummary;

function ticket(id: string, status: string): TicketDetailDto {
  return {
    id, ticketNumber: '100001', title: 'Izvještaj', description: '<p>x</p>', status, clientId: 'c', clientName: 'Client', companyId: 'co', companyName: 'Acme',
    createdAt: new Date(2026, 0, 5).toISOString(), attachments: [], messages: [], assignees: [], workSessions: [], priority: 'Medium',
  } as unknown as TicketDetailDto;
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/tickets/t1']}>
      <Routes>
        <Route path="/tickets/:id" element={<TicketDetailPage />} />
      </Routes>
    </MemoryRouter>
  );
}

// NOVO-FE-01: a reply that arrived after the agent had pressed "Start work" used the ticket of the moment the reply box was drawn,
// and put the whole ticket (its status, its assignees) back to how it was then.
describe('the ticket page', () => {
  beforeEach(() => {
    firstDrawn.length = 0;
    useAuthStore.setState({ accessToken: 't', user: employee });
    vi.mocked(ticketsApi.getById).mockReset().mockResolvedValue(ticket('t1', 'Open'));
  });

  it('adds a reply that arrives late to the ticket as it is now, not as it was when the reply was started', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByTestId('feed')).toHaveTextContent('Open|0'));

    await userEvent.click(screen.getByRole('button', { name: 'start work' }));
    await waitFor(() => expect(screen.getByTestId('feed')).toHaveTextContent('InProgress|0'));

    await userEvent.click(screen.getByRole('button', { name: 'late reply' }));

    expect(screen.getByTestId('feed')).toHaveTextContent('InProgress|1');
  });
});
