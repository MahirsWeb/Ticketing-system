import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import { QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { renderWithQuery } from '../test/renderWithQuery';
import { useTicket, useTicketCounts, useTicketList } from './ticketQueries';
import { ticketsApi } from './tickets';
import { queryClient } from './queryClient';
import { apiClient } from './client';

vi.mock('./client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const asked = (url: string) => get.mock.calls.filter((c) => c[0] === url).length;

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

function QueryProvider({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

function Counts() {
  const { data } = useTicketCounts();
  return <p data-testid="counts">{data ? `total ${data.total}` : 'loading'}</p>;
}

function List({ status }: { status?: string }) {
  const { data } = useTicketList({ status });
  return <p data-testid={`list-${status ?? 'all'}`}>{data ? data.items.map((i) => i.title).join(',') : 'loading'}</p>;
}

function One({ id }: { id: string }) {
  const { data, isError } = useTicket(id);
  return <p data-testid="one">{isError ? 'failed' : data ? data.title : 'loading'}</p>;
}

describe('the ticket data the screens share (T-17)', () => {
  let totals: number[];

  beforeEach(() => {
    queryClient.clear();
    get.mockReset();
    post.mockReset();
    totals = [4, 3, 2, 1];
    get.mockImplementation(async (url: string) => {
      if (url === '/api/tickets/counts') return { data: { total: totals.shift() ?? 0, mine: 0, opened: 0, closed: 0, new: 0 } } as never;
      if (url === '/api/tickets') return { data: { items: [{ id: 'a', title: 'Printer' }], totalCount: 1, page: 1, pageSize: 20 } } as never;
      return { data: { id: 't1', title: 'First' } } as never;
    });
    post.mockResolvedValue({ data: { id: 't1', title: 'First', status: 'Closed' } } as never);
  });
  afterEach(() => queryClient.clear());

  it('asks the same list once however many screens show it, and a different list on its own', async () => {
    renderWithQuery(
      <>
        <List />
        <List />
        <List status="New" />
      </>,
      queryClient
    );

    await waitFor(() => expect(screen.getAllByText('Printer')).toHaveLength(3));
    expect(get.mock.calls.filter((c) => c[0] === '/api/tickets').map((c) => (c[1] as { params: { status?: string } }).params.status)).toEqual(
      expect.arrayContaining([undefined, 'New'])
    );
    expect(asked('/api/tickets')).toBe(2);
  });

  it('asks again for the lists and the numbers on screen once a ticket has been changed', async () => {
    renderWithQuery(
      <>
        <Counts />
        <List />
      </>,
      queryClient
    );
    await screen.findByText('total 4');
    await screen.findByText('Printer');

    await act(async () => {
      await ticketsApi.close('t1', 'Zamijenjen toner');
    });

    expect(await screen.findByText('total 3')).toBeInTheDocument();
    expect(asked('/api/tickets/counts')).toBe(2);
    expect(asked('/api/tickets')).toBe(2);
  });

  it.each([
    ['opening', () => ticketsApi.open('t1', {} as never)],
    ['reopening', () => ticketsApi.reopen('t1')],
    ['changing the status of', () => ticketsApi.setStatus('t1', 'InProgress')],
    ['moving to another branch', () => ticketsApi.transferBranch('t1', 'b')],
    ['adding somebody to', () => ticketsApi.addAssignee('t1', 'u')],
    ['replying on', () => ticketsApi.addMessage('t1', 'Response', '<p>x</p>')],
    ['creating', () => ticketsApi.create('Title', '<p>x</p>', 'b')],
  ])('also does so after %s a ticket', async (_what, change) => {
    renderWithQuery(<Counts />, queryClient);
    await screen.findByText('total 4');

    await act(async () => {
      await change();
    });

    expect(await screen.findByText('total 3')).toBeInTheDocument();
  });

  it('does not ask again when the change failed, because nothing changed', async () => {
    renderWithQuery(<Counts />, queryClient);
    await screen.findByText('total 4');
    post.mockRejectedValueOnce(new Error('refused'));

    await act(async () => {
      await expect(ticketsApi.close('t1', 'x')).rejects.toThrow('refused');
    });

    expect(asked('/api/tickets/counts')).toBe(1);
    expect(screen.getByTestId('counts')).toHaveTextContent('total 4');
  });

  it("does not ask again for the ticket that was changed, because the server's answer is the whole ticket", async () => {
    renderWithQuery(
      <>
        <One id="t1" />
        <Counts />
      </>,
      queryClient
    );
    await screen.findByText('First');

    await act(async () => {
      await ticketsApi.close('t1', 'x');
    });

    await screen.findByText('total 3');
    expect(asked('/api/tickets/t1')).toBe(1);
  });

  it('never shows a list from memory: leaving it and coming back shows the loading state and then the current list', async () => {
    const first = renderWithQuery(<List />, queryClient);
    await screen.findByText('Printer');
    first.unmount();
    await new Promise((r) => setTimeout(r, 10)); // going to another screen and coming back takes longer than the moment the list is forgotten in
    const later = deferred<{ data: unknown }>();
    get.mockImplementation((() => later.promise) as never);

    renderWithQuery(<List />, queryClient);

    expect(screen.getByTestId('list-all')).toHaveTextContent('loading');
    await act(async () => later.resolve({ data: { items: [{ id: 'b', title: 'Scanner' }], totalCount: 1, page: 1, pageSize: 20 } }));
    expect(await screen.findByText('Scanner')).toBeInTheDocument();
    expect(asked('/api/tickets')).toBe(2); // once for the first visit and once more for the second
  });

  it('never shows a ticket from memory either: coming back to it shows the loading state and then the current ticket', async () => {
    const first = renderWithQuery(<One id="t1" />, queryClient);
    await screen.findByText('First');
    first.unmount();
    await new Promise((r) => setTimeout(r, 10));
    const later = deferred<{ data: unknown }>();
    get.mockImplementation((() => later.promise) as never);

    renderWithQuery(<One id="t1" />, queryClient);

    expect(screen.getByTestId('one')).toHaveTextContent('loading');
    await act(async () => later.resolve({ data: { id: 't1', title: 'First, changed meanwhile' } }));
    expect(await screen.findByText('First, changed meanwhile')).toBeInTheDocument();
  });

  it('forgets a ticket as soon as another one is opened, so the first is never shown under the address of the second', async () => {
    const view = renderWithQuery(<One id="t1" />, queryClient);
    await screen.findByText('First');
    const second = deferred<{ data: unknown }>();
    get.mockImplementation((() => second.promise) as never);

    view.rerender(
      <QueryProvider>
        <One id="t2" />
      </QueryProvider>
    );

    expect(screen.getByTestId('one')).toHaveTextContent('loading');
    expect(screen.queryByText('First')).not.toBeInTheDocument();
    await act(async () => second.resolve({ data: { id: 't2', title: 'Second' } }));
    expect(await screen.findByText('Second')).toBeInTheDocument();
  });

  it('asks for nothing while there is no ticket to ask for', async () => {
    renderWithQuery(<One id="" />, queryClient);

    await new Promise((r) => setTimeout(r, 20));

    expect(get).not.toHaveBeenCalled();
  });
});
