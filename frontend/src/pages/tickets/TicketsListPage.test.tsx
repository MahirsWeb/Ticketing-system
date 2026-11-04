import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import TicketsListPage from './TicketsListPage';
import { ticketsApi } from '../../api/tickets';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { useAuthStore } from '../../store/authStore';
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

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/tickets']}>
      <TicketsListPage />
    </MemoryRouter>
  );
}

describe('the tickets list', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    list.mockReset();
    vi.mocked(lookupsApi.departments).mockResolvedValue([]);
    vi.mocked(usersApi.list).mockResolvedValue([]);
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

  it('says so, instead of failing silently, when the list cannot be loaded', async () => {
    list.mockRejectedValue(new Error('network'));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('could not be loaded');
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('0 tickets'));
  });
});
