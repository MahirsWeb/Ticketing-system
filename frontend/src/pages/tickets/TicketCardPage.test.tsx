import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TicketCardPage from './TicketCardPage';
import { documentsApi } from '../../api/documents';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { TicketCardDto } from '../../types';

vi.mock('../../api/documents', () => ({ documentsApi: { ticketCard: vi.fn(), ticketCardXlsx: vi.fn(), monthly: vi.fn(), monthlyXlsx: vi.fn() } }));

const staffCard: TicketCardDto = {
  asOfUtc: '2026-11-20T12:00:00Z',
  header: {
    id: 't1', ticketNumber: 'TKT-7', title: 'Printer ne radi', status: 'Closed', company: 'Alfa', client: 'Klijent Prvi', branch: 'IT', assignee: 'Amra Amić', priority: 'High',
    createdAt: '2026-11-02T08:00:00Z', openedAtUtc: '2026-11-02T09:00:00Z', closedAtUtc: '2026-11-02T14:00:00Z', resolutionHours: 5, dueDateUtc: '2026-11-02T16:00:00Z', slaMet: true,
    staffReplies: 2, clientReplies: 1, internalNotes: 3, workHours: 1.5,
  },
  events: [
    { atUtc: '2026-11-02T08:00:00Z', kind: 'Created', person: 'Klijent Prvi', from: null, to: null, durationSeconds: null, running: false },
    { atUtc: '2026-11-02T09:00:00Z', kind: 'Opened', person: 'Amra Amić', from: null, to: null, durationSeconds: null, running: false },
    { atUtc: '2026-11-02T10:00:00Z', kind: 'StatusChanged', person: 'Amra Amić', from: 'Open', to: 'InProgress', durationSeconds: null, running: false },
    { atUtc: '2026-11-02T10:30:00Z', kind: 'WorkSession', person: 'Amra Amić', from: null, to: null, durationSeconds: 5400, running: false },
    { atUtc: '2026-11-02T11:00:00Z', kind: 'InternalNote', person: 'Amra Amić', from: null, to: null, durationSeconds: null, running: false },
    { atUtc: '2026-11-02T12:00:00Z', kind: 'Transferred', person: 'Admin Admin', from: null, to: 'Prodaja', durationSeconds: null, running: false },
    { atUtc: '2026-11-02T14:00:00Z', kind: 'Closed', person: 'Admin Admin', from: null, to: null, durationSeconds: null, running: false },
  ],
  spans: [
    { status: 'New', fromUtc: '2026-11-02T08:00:00Z', toUtc: '2026-11-02T09:00:00Z', seconds: 3600 },
    { status: 'Open', fromUtc: '2026-11-02T09:00:00Z', toUtc: '2026-11-02T10:00:00Z', seconds: 3600 },
    { status: null, fromUtc: '2026-11-02T10:00:00Z', toUtc: '2026-11-02T14:00:00Z', seconds: 14400 },
    { status: 'Closed', fromUtc: '2026-11-02T14:00:00Z', toUtc: null, seconds: 1234567 },
  ],
  statusTotals: [{ status: 'New', seconds: 3600 }, { status: 'Open', seconds: 3600 }, { status: 'Closed', seconds: 1234567 }],
  unknownSeconds: 14400,
  trailComplete: false,
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(documentsApi.ticketCard).mockResolvedValue(staffCard);
  vi.mocked(documentsApi.ticketCardXlsx).mockResolvedValue(undefined);
  window.print = vi.fn();
});

function renderCard() {
  return renderWithQuery(
    <MemoryRouter initialEntries={['/tickets/t1/card']}>
      <Routes>
        <Route path="/tickets/:id/card" element={<TicketCardPage />} />
        <Route path="/tickets/:id" element={<p>the ticket</p>} />
      </Routes>
    </MemoryRouter>
  );
}

describe('the card of a ticket', () => {
  it('shows every event once, oldest first, with who did it and what changed', async () => {
    renderCard();

    expect(await screen.findByRole('heading', { name: '#TKT-7 — Printer ne radi' })).toBeInTheDocument();
    const events = screen.getAllByRole('listitem').filter((li) => li.querySelector('time'));
    expect(events.map((e) => e.querySelector('span')?.textContent)).toEqual(['Created', 'Opened', 'Status changed', 'Work session', 'Internal note', 'Transferred to another branch', 'Closed']);
    expect(within(events[2]).getByText(/Open → InProgress/)).toBeInTheDocument();
    expect(within(events[3]).getByText(/1 h 30 min/)).toBeInTheDocument();
    expect(within(events[5]).getByText(/\(unrouted\) → Prodaja/)).toBeInTheDocument();
    expect(within(events[1]).getByText(/Amra Amić/)).toBeInTheDocument();
    expect(screen.getByText(/their text is not on the card/)).toBeInTheDocument(); // what is not on it is said
  });

  it('shows the time in each status and shows what the history does not say as unknown, with a notice', async () => {
    renderCard();

    const table = (await screen.findByRole('table')).closest('div')!;
    const rows = within(table).getAllByRole('row').slice(1);
    expect(within(rows[0]).getAllByRole('cell').map((c) => c.textContent)).toEqual(['New', expect.any(String), expect.any(String), '1 h 00 min']);
    expect(within(rows[2]).getByText('Unknown')).toBeInTheDocument();
    expect(within(rows[3]).getByText('still')).toBeInTheDocument();
    expect(screen.getByText(/history of this ticket is incomplete/)).toBeInTheDocument();
  });

  it('shows the facts of the header: the time to solve, the SLA, the replies and the hours', async () => {
    renderCard();

    expect(await screen.findByText('5 h')).toBeInTheDocument();
    expect(screen.getByText('Yes')).toBeInTheDocument();
    expect(screen.getByText('Internal notes')).toBeInTheDocument();
    expect(screen.getByText('1.5 h')).toBeInTheDocument();
    expect(screen.getByText('IT')).toBeInTheDocument();
  });

  it('prints, downloads, and leads back to the ticket; the buttons are not in the print', async () => {
    const user = userEvent.setup();
    renderCard();
    const bar = (await screen.findByRole('button', { name: /Print/ })).closest('div')!.parentElement!;
    expect(bar.className).toContain('print:hidden');

    await user.click(screen.getByRole('button', { name: /Print/ }));
    await user.click(screen.getByRole('button', { name: /Download/ }));

    expect(window.print).toHaveBeenCalled();
    await waitFor(() => expect(vi.mocked(documentsApi.ticketCardXlsx)).toHaveBeenCalledWith('t1'));
    await user.click(screen.getByRole('link', { name: /Back to the ticket/ }));
    expect(await screen.findByText('the ticket')).toBeInTheDocument();
  });

  it('says so when the download fails', async () => {
    vi.mocked(documentsApi.ticketCardXlsx).mockRejectedValue(new Error('boom'));
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole('button', { name: /Download/ }));

    expect(await screen.findByText('The work sheet could not be made. Try again.')).toBeInTheDocument();
  });

  it('says what the server said when a client is refused, and shows nothing of the card', async () => {
    vi.mocked(documentsApi.ticketCard).mockRejectedValue({ response: { status: 403, data: { message: 'You do not have access.' } } });
    renderCard();

    expect(await screen.findByText('You do not have access.')).toBeInTheDocument();
    expect(screen.queryByText('Timeline')).not.toBeInTheDocument();
  });

  it('says what the server said when the card cannot be made', async () => {
    vi.mocked(documentsApi.ticketCard).mockRejectedValue({ response: { data: { message: 'Nope.' } } });
    renderCard();

    expect(await screen.findByText('Nope.')).toBeInTheDocument();
  });
});
