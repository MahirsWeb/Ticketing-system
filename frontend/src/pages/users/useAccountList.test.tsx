import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useAccountList } from './useAccountList';
import { useUserPage } from '../../api/userQueries';

vi.mock('../../api/userQueries', () => ({ useUserPage: vi.fn(() => ({})) }));

const asked = () => vi.mocked(useUserPage).mock.calls.at(-1)![0];

describe('what the list of accounts is for', () => {
  beforeEach(() => vi.mocked(useUserPage).mockClear());

  it('starts with the first page of clients, 25 to a page', () => {
    renderHook(() => useAccountList(true));

    expect(asked()).toEqual({ group: 'clients', search: undefined, page: 1, pageSize: 25 });
  });

  it('lets somebody who can see the staff choose them', () => {
    const { result } = renderHook(() => useAccountList(true));

    act(() => result.current.setTab('employees'));

    expect(result.current.activeTab).toBe('employees');
    expect(asked().group).toBe('staff');
  });

  it('keeps somebody who cannot see the staff with the clients, whatever tab is chosen', () => {
    const { result } = renderHook(() => useAccountList(false));

    act(() => result.current.setTab('employees'));

    expect(result.current.activeTab).toBe('clients');
    expect(asked().group).toBe('clients');
  });

  it('asks for the page that was chosen, and goes back to the first when the tab is changed', () => {
    const { result } = renderHook(() => useAccountList(true));
    act(() => result.current.setPage(3));
    expect(asked().page).toBe(3);

    act(() => result.current.setTab('employees'));

    expect(asked()).toMatchObject({ group: 'staff', page: 1 });
    expect(result.current.page).toBe(1);
  });

  it('does not bring back the page of a tab that was left when that tab is chosen again', () => {
    const { result } = renderHook(() => useAccountList(true));
    act(() => result.current.setPage(3));
    act(() => result.current.setTab('employees'));

    act(() => result.current.setTab('clients'));

    expect(result.current.page).toBe(1);
    expect(asked()).toMatchObject({ group: 'clients', page: 1 });
  });
});
