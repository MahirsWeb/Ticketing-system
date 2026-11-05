import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithQuery } from '../test/renderWithQuery';
import { useEmployees, useUserPage } from './userQueries';
import { userKeys } from './userKeys';
import { usersApi } from './users';
import { queryClient } from './queryClient';
import { apiClient } from './client';
import type { UserPageDto } from '../types';

vi.mock('./client', () => ({ apiClient: { get: vi.fn(), post: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const asked = () => get.mock.calls.filter((c) => c[0] === '/api/users/paged');

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const answer = (group: 'clients' | 'staff', page = 1): { data: UserPageDto } => ({
  data: {
    items: [{ id: `${group}-${page}`, firstName: group, lastName: String(page) } as never],
    page, pageSize: 25, totalCount: 30, clientCount: 7, staffCount: 3,
  },
});

function Page({ group = 'clients', page = 1, tag = 'page' }: { group?: 'clients' | 'staff'; page?: number; tag?: string }) {
  const q = useUserPage({ group, page, pageSize: 25 });
  return (
    <p data-testid={tag}>
      {q.isError ? 'failed' : q.data ? q.data.items.map((i) => i.id).join(',') : 'loading'}
      {q.isPlaceholderData ? ' (placeholder)' : ''}
    </p>
  );
}
function Staff({ departmentId, subBranchId, tag }: { departmentId?: string; subBranchId?: string; tag: string }) {
  const q = useEmployees({ departmentId, subBranchId });
  return <p data-testid={tag}>{q.data ? q.data.map((u) => u.id).join(',') : 'loading'}</p>;
}
function Switch() {
  const [group, setGroup] = useState<'clients' | 'staff'>('clients');
  return (
    <>
      <button onClick={() => setGroup('staff')}>staff</button>
      <Page group={group} />
    </>
  );
}

describe('the account data the screens share (T-17)', () => {
  beforeEach(() => {
    queryClient.clear();
    get.mockReset();
    post.mockReset();
    get.mockImplementation((async (_url: string, config: { params: { group: 'clients' | 'staff'; page: number } }) => answer(config.params.group, config.params.page)) as never);
  });
  afterEach(() => queryClient.clear());

  it('asks the same page once however many places show it, and another page on its own', async () => {
    renderWithQuery(
      <>
        <Page tag="a" />
        <Page tag="b" />
        <Page tag="staff" group="staff" />
      </>,
      queryClient
    );

    await waitFor(() => expect(screen.getByTestId('staff')).toHaveTextContent('staff-1'));
    expect(asked()).toHaveLength(2);
  });

  it('keeps the staff of each branch, and of each sub-branch of it, as an entry of its own, and asks for each once', async () => {
    get.mockImplementation((async (url: string, config?: { params?: { departmentId?: string; subBranchId?: string } }) => {
      if (url === '/api/users') return { data: [{ id: `${config?.params?.departmentId ?? 'all'}/${config?.params?.subBranchId ?? '-'}` }] };
      return answer('clients');
    }) as never);
    renderWithQuery(
      <>
        <Staff tag="all" />
        <Staff tag="a" departmentId="a" />
        <Staff tag="a-again" departmentId="a" />
        <Staff tag="a-s1" departmentId="a" subBranchId="s1" />
        <Staff tag="b" departmentId="b" />
      </>,
      queryClient
    );

    await waitFor(() => expect(screen.getByTestId('b')).toHaveTextContent('b/-'));
    expect([screen.getByTestId('all').textContent, screen.getByTestId('a').textContent, screen.getByTestId('a-again').textContent, screen.getByTestId('a-s1').textContent]).toEqual(['all/-', 'a/-', 'a/-', 'a/s1']);
    expect(get.mock.calls.filter((c) => c[0] === '/api/users')).toHaveLength(4);
    expect(get.mock.calls.find((c) => (c[1] as { params: { subBranchId?: string } }).params.subBranchId === 's1')![1]).toEqual({ params: { role: 'Employee', departmentId: 'a', subBranchId: 's1' } });
  });

  it('forgets a page when it is left, so that coming back to it asks again', async () => {
    const first = renderWithQuery(<Page />, queryClient);
    await screen.findByText('clients-1');
    first.unmount();
    await waitFor(() => expect(queryClient.getQueryData(userKeys.page({ group: 'clients', page: 1, pageSize: 25 }))).toBeUndefined());

    renderWithQuery(<Page />, queryClient);

    await screen.findByText('clients-1');
    expect(asked()).toHaveLength(2);
  });

  it('keeps showing the page before while the next one is on its way, and says so, so that the totals do not flash', async () => {
    const user = userEvent.setup();
    const slow = deferred<{ data: UserPageDto }>();
    renderWithQuery(<Switch />, queryClient);
    await screen.findByText('clients-1');
    get.mockImplementation((() => slow.promise) as never);

    await user.click(screen.getByRole('button', { name: 'staff' }));

    expect(screen.getByTestId('page')).toHaveTextContent('clients-1 (placeholder)');
    await act(async () => slow.resolve(answer('staff')));
    await waitFor(() => expect(screen.getByTestId('page')).toHaveTextContent(/^staff-1$/));
  });

  it('cancels the request for a page that has been overtaken, and hands the request a way to know it', async () => {
    const user = userEvent.setup();
    const slow = deferred<{ data: UserPageDto }>();
    get.mockImplementation((() => slow.promise) as never);
    renderWithQuery(<Switch />, queryClient);
    await waitFor(() => expect(asked()).toHaveLength(1));
    const signal = (asked()[0][1] as { signal: AbortSignal }).signal;
    expect(signal.aborted).toBe(false);

    await user.click(screen.getByRole('button', { name: 'staff' }));

    await waitFor(() => expect(signal.aborted).toBe(true));
    await act(async () => slow.resolve(answer('clients')));
  });

  describe('making an account', () => {
    const made = { data: { userId: 'n1', email: 'a@b.test', temporaryPassword: 'x', temporaryPasswordExpiresAtUtc: '2026-06-01T00:00:00Z' } };

    it.each([
      ['a client', () => usersApi.createClient('Pero', 'Perić', 'p@b.test', 'c1'), '/api/users/clients'],
      ['an employee', () => usersApi.createEmployee('Sara', 'Sarić', 's@b.test', 'Employee', 'd1', undefined), '/api/users/employees'],
    ])('asks the lists of accounts again when %s is made', async (_name, make, url) => {
      renderWithQuery(<Page />, queryClient);
      await screen.findByText('clients-1');
      post.mockResolvedValue(made as never);

      await act(async () => {
        await make();
      });

      expect(post.mock.calls.map((c) => c[0])).toEqual([url]);
      await waitFor(() => expect(asked()).toHaveLength(2));
    });

    it('sends what it was given, and nothing in place of a branch that was left out', async () => {
      post.mockResolvedValue(made as never);

      await usersApi.createEmployee('Sara', 'Sarić', 's@b.test', 'Admin');

      expect(post.mock.calls).toEqual([['/api/users/employees', { firstName: 'Sara', lastName: 'Sarić', email: 's@b.test', role: 'Admin', departmentId: null, subBranchId: null }]]);
    });

    it('does not ask again when the server refuses', async () => {
      renderWithQuery(<Page />, queryClient);
      await screen.findByText('clients-1');
      post.mockRejectedValue(new Error('refused'));

      await act(async () => {
        await usersApi.createClient('Pero', 'Perić', 'p@b.test', 'c1').catch(() => undefined);
      });

      expect(asked()).toHaveLength(1);
    });
  });
});
