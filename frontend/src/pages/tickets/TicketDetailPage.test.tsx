import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import TicketDetailPage from './TicketDetailPage';
import { ticketsApi } from '../../api/tickets';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
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

function ticket(id: string, status: string, title = 'Izvještaj'): TicketDetailDto {
  return {
    id, ticketNumber: '100001', title, description: '<p>x</p>', status, clientId: 'c', clientName: 'Client', companyId: 'co', companyName: 'Acme',
    createdAt: new Date(2026, 0, 5).toISOString(), attachments: [], messages: [], assignees: [], workSessions: [], priority: 'Medium',
  } as unknown as TicketDetailDto;
}

function GoToSecond() {
  const navigate = useNavigate();
  return <button onClick={() => navigate('/tickets/t2')}>open the second</button>;
}

function renderPage() {
  return renderWithQuery(
    <MemoryRouter initialEntries={['/tickets/t1']}>
      <GoToSecond />
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

  it('gives every part of the page a key of its own, so none is dropped or doubled when the ticket changes', async () => {
    // The knowledge-base panel and the close form were both keyed by the ticket's id, and React does not promise to keep two children with one key.
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {});
    try {
      renderPage();
      await waitFor(() => expect(screen.getByTestId('feed')).toBeInTheDocument());

      expect(errors.mock.calls.flat().join(' ')).not.toContain('same key');
    } finally {
      errors.mockRestore();
    }
  });

  it('does not say the ticket was not found while it is still being loaded', async () => {
    let arrive!: (t: TicketDetailDto) => void;
    vi.mocked(ticketsApi.getById).mockReset().mockReturnValue(
      new Promise<TicketDetailDto>((resolve) => {
        arrive = resolve;
      })
    );

    renderPage();

    expect(screen.queryByText('Ticket not found.')).not.toBeInTheDocument();
    arrive(ticket('t1', 'Open', 'Arrived'));
    expect(await screen.findByText(/Arrived/)).toBeInTheDocument();
    expect(screen.queryByText('Ticket not found.')).not.toBeInTheDocument();
  });

  it('forgets the ticket it was showing as soon as another one is opened, and shows the other when it arrives', async () => {
    let arrive!: (t: TicketDetailDto) => void;
    const second = new Promise<TicketDetailDto>((resolve) => {
      arrive = resolve;
    });
    vi.mocked(ticketsApi.getById).mockReset().mockImplementation((id) => (id === 't1' ? Promise.resolve(ticket('t1', 'Open', 'First ticket')) : second));
    renderPage();
    await screen.findByText(/First ticket/);

    await userEvent.click(screen.getByRole('button', { name: 'open the second' }));

    expect(screen.queryByText(/First ticket/)).not.toBeInTheDocument();
    expect(screen.queryByTestId('feed')).not.toBeInTheDocument();
    arrive(ticket('t2', 'Open', 'Second ticket'));
    expect(await screen.findByText(/Second ticket/)).toBeInTheDocument();
  });

  it('says what the server said when the ticket cannot be loaded, and something plain when it said nothing', async () => {
    vi.mocked(ticketsApi.getById).mockReset().mockRejectedValue({ response: { data: { message: 'This ticket belongs to another branch.' } } });
    const first = renderPage();
    expect(await screen.findByText('This ticket belongs to another branch.')).toBeInTheDocument();
    first.unmount();

    vi.mocked(ticketsApi.getById).mockReset().mockRejectedValue(new Error('network'));
    renderPage();
    expect(await screen.findByText('Could not load this ticket.')).toBeInTheDocument();
  });
});
