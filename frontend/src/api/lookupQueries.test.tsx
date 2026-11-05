import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { QueryClientProvider } from '@tanstack/react-query';
import { renderWithQuery } from '../test/renderWithQuery';
import { useDepartments, useSubBranches } from './lookupQueries';
import { lookupKeys } from './lookupKeys';
import { lookupsApi } from './lookups';
import { createQueryClient, queryClient } from './queryClient';
import { apiClient } from './client';
import { useAuthStore } from '../store/authStore';

vi.mock('./client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));

const get = vi.mocked(apiClient.get);

function Names() {
  const { data = [] } = useDepartments();
  return (
    <ul>
      {data.map((d) => (
        <li key={d.id}>{d.name}</li>
      ))}
    </ul>
  );
}

function SubBranches({ departmentId }: { departmentId?: string }) {
  const { data = [] } = useSubBranches(departmentId);
  return (
    <ul aria-label="sub-branches">
      {data.map((s) => (
        <li key={s.id}>{s.name}</li>
      ))}
    </ul>
  );
}

describe('the shared lists of branches, firms and the like (T-17)', () => {
  beforeEach(() => {
    queryClient.clear();
    get.mockReset();
    vi.mocked(apiClient.post).mockReset();
  });
  afterEach(() => queryClient.clear());

  it('are fetched once however many screens show them', async () => {
    get.mockResolvedValue({ data: [{ id: 'a', name: 'Branch A' }] } as never);

    renderWithQuery(
      <>
        <Names />
        <Names />
      </>,
      queryClient
    );

    expect(await screen.findAllByText('Branch A')).toHaveLength(2);
    expect(get).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledWith('/api/departments');
  });

  it('are not asked for again when a screen is opened a little later', async () => {
    get.mockResolvedValue({ data: [{ id: 'a', name: 'Branch A' }] } as never);
    const first = renderWithQuery(<Names />, queryClient);
    await screen.findByText('Branch A');
    first.unmount();

    renderWithQuery(<Names />, queryClient);

    expect(await screen.findByText('Branch A')).toBeInTheDocument(); // there at once, from what was fetched before
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('are asked for again after one of them was changed, so nobody keeps looking at an old list', async () => {
    get.mockResolvedValue({ data: [{ id: 'a', name: 'Branch A' }] } as never);
    renderWithQuery(<Names />, queryClient);
    await screen.findByText('Branch A');

    get.mockResolvedValue({ data: [{ id: 'a', name: 'Branch A' }, { id: 'b', name: 'Branch B' }] } as never);
    vi.mocked(apiClient.post).mockResolvedValue({ data: { id: 'b', name: 'Branch B' } } as never);
    await lookupsApi.createDepartment('Branch B', 'b@example.test');

    expect(await screen.findByText('Branch B')).toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('keep the sub-branches of each branch apart and ask for none while no branch is chosen', async () => {
    get.mockImplementation(async (url: string) => ({ data: url.includes('/a/') ? [{ id: 'sa', name: 'Sub of A' }] : [{ id: 'sb', name: 'Sub of B' }] }) as never);

    const view = renderWithQuery(<SubBranches />, queryClient);
    expect(get).not.toHaveBeenCalled();

    const inClient = (ui: React.ReactElement) => <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>;
    view.rerender(inClient(<SubBranches departmentId="a" />));
    expect(await screen.findByText('Sub of A')).toBeInTheDocument();

    view.rerender(inClient(<SubBranches departmentId="b" />));
    await waitFor(() => expect(screen.getByText('Sub of B')).toBeInTheDocument());
    expect(screen.queryByText('Sub of A')).not.toBeInTheDocument(); // the answer for the branch that is no longer chosen is never shown
  });

  it('are forgotten when the person signs out, so the next one never sees them', () => {
    queryClient.setQueryData(lookupKeys.departments, [{ id: 'a', name: 'Branch A' }]);
    useAuthStore.setState({ accessToken: 'token' } as never);
    expect(queryClient.getQueryData(lookupKeys.departments)).toBeDefined();

    useAuthStore.setState({ accessToken: null } as never);

    expect(queryClient.getQueryData(lookupKeys.departments)).toBeUndefined();
  });
});

describe('what is tried again (T-17)', () => {
  const retry = createQueryClient().getDefaultOptions().queries!.retry as (count: number, error: unknown) => boolean;
  const answered = (status: number) => new AxiosError('failed', 'ERR_BAD_RESPONSE', undefined, undefined, { status } as never);

  it('a request the server refused is not repeated', () => {
    expect(retry(0, answered(404))).toBe(false);
    expect(retry(0, answered(403))).toBe(false);
  });

  it('a failure on the server or on the way is repeated, but not for ever', () => {
    expect(retry(0, answered(500))).toBe(true);
    expect(retry(1, new Error('network down'))).toBe(true);
    expect(retry(2, answered(500))).toBe(false);
  });
});
