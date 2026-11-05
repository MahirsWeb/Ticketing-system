import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import TicketsListPage from './TicketsListPage';
import { ticketsApi } from '../../api/tickets';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { TicketListItem } from '../../types';

vi.mock('../../api/tickets', () => ({ ticketsApi: { list: vi.fn() } }));
vi.mock('../../api/lookups', () => ({ lookupsApi: { departments: vi.fn() } }));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn() } }));

const list = vi.mocked(ticketsApi.list);

function ticket(number: string, title: string): TicketListItem {
  return {
    id: number, ticketNumber: number, title, status: 'Open', priority: 'Medium', clientName: 'Client', companyName: 'Acme',
    departmentName: 'Branch', assignedToName: null, createdAt: new Date(2026, 0, 5).toISOString(), dueDateUtc: null, hasUnreadResponse: false,
  } as unknown as TicketListItem;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const page = (items: TicketListItem[]) => ({ items, totalCount: items.length, page: 1, pageSize: 20 });
const searchesAsked = () => list.mock.calls.map((c) => c[0].search ?? null);

function renderPage(address = '/tickets') {
  return renderWithQuery(
    <MemoryRouter initialEntries={[address]}>
      <TicketsListPage />
    </MemoryRouter>
  );
}

describe('the tickets list', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    list.mockReset();
    vi.mocked(lookupsApi.departments).mockResolvedValue([]);
    vi.mocked(usersApi.list).mockReset().mockResolvedValue([]);
    useAuthStore.setState({ accessToken: 't', user: { id: 'u1', role: 'Employee', firstName: 'A', lastName: 'B' } as never });
  });
  afterEach(() => vi.useRealTimers());

  it('asks the server once for what was typed, not once per keystroke (TS-17.2)', async () => {
    list.mockResolvedValue(page([ticket('100001', 'Printer')]));
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderPage();
    await screen.findByText('Printer');

    await user.type(screen.getByLabelText('Search tickets by number or title'), 'Izv');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(400);
    });

    expect(searchesAsked()).toEqual([null, 'Izv']);
  });

  it('shows the answer to the latest search even when an earlier one arrives last (TS-17.2)', async () => {
    const first = deferred<ReturnType<typeof page>>();
    const second = deferred<ReturnType<typeof page>>();
    list.mockResolvedValueOnce(page([])).mockReturnValueOnce(first.promise as never).mockReturnValueOnce(second.promise as never);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderPage();

    const box = screen.getByLabelText('Search tickets by number or title');
    await user.type(box, 'Iz');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(400);
    });
    await user.type(box, 'v');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(400);
    });
    expect(searchesAsked()).toEqual([null, 'Iz', 'Izv']);

    second.resolve(page([ticket('100002', 'Izvještaj se ne generiše')]));
    await screen.findByText('Izvještaj se ne generiše');
    first.resolve(page([ticket('100003', 'Something older')]));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(50);
    });

    expect(screen.getByText('Izvještaj se ne generiše')).toBeInTheDocument();
    expect(screen.queryByText('Something older')).not.toBeInTheDocument();
  });

  it('asks the server for exactly what the address says', async () => {
    list.mockResolvedValue(page([]));

    renderPage('/tickets?status=Closed&dept=d1&assignee=e1&q=toner&sort=title&dir=desc&page=2&from=2026-01-01T00:00:00.000Z&to=2026-02-01T00:00:00.000Z');

    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(list.mock.calls.at(-1)![0]).toEqual({
      page: 2,
      pageSize: 20,
      status: 'Closed',
      answeredOnly: false,
      departmentId: 'd1',
      assignedToUserId: 'e1',
      search: 'toner',
      createdFrom: '2026-01-01T00:00:00.000Z',
      createdTo: '2026-02-01T00:00:00.000Z',
      sortBy: 'title',
      sortDir: 'desc',
    });
  });

  it('asks for the tickets of the person who is signed in when "mine" is chosen, except among the new ones, which nobody has yet', async () => {
    list.mockResolvedValue(page([]));

    const mine = renderPage('/tickets?mine=1&status=Open,InProgress,Resolved');
    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(list.mock.calls.at(-1)![0].assignedToUserId).toBe('u1');
    mine.unmount();

    list.mockClear();
    renderPage('/tickets?mine=1&status=New');
    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(list.mock.calls.at(-1)![0].assignedToUserId).toBeUndefined();
  });

  it('does not ask for the list of staff when the person is a client, who has no use for it', async () => {
    useAuthStore.setState({ accessToken: 't', user: { id: 'c1', role: 'Client', firstName: 'A', lastName: 'B' } as never });
    list.mockResolvedValue(page([ticket('100001', 'Printer')]));

    renderPage();

    expect(await screen.findByText('Printer')).toBeInTheDocument();
    expect(usersApi.list).not.toHaveBeenCalled();
  });

  it('asks for the list of staff when the person is staff, for the "assigned to" filter', async () => {
    list.mockResolvedValue(page([]));

    renderPage();

    await waitFor(() => expect(usersApi.list).toHaveBeenCalledWith({ role: 'Employee' }));
  });

  it('asks only for the tickets that have been answered on the "answered" tab', async () => {
    list.mockResolvedValue(page([]));

    renderPage('/tickets?answered=1&status=Open,InProgress,Resolved');

    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(list.mock.calls.at(-1)![0].answeredOnly).toBe(true);
  });

  it('says so, instead of failing silently, when the list cannot be loaded', async () => {
    list.mockRejectedValue(new Error('network'));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('could not be loaded');
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('0 tickets'));
  });
});
