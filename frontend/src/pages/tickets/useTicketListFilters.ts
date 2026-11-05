import { useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { TicketListParams } from '../../api/tickets';
import { useDebouncedValue } from '../../lib/useDebouncedValue';
import { PAGE_SIZE, parseDate, parsePage, tabOf, tabToParams, type TicketTab } from './ticketListAddress';

/**
 * What the list of tickets is filtered, sorted and paged by. All of it lives in the address, so a refresh, the back button, a bookmark or a
 * link sent to a colleague shows the same list. `params` is what the server is asked for.
 */
export function useTicketListFilters({ userId, isEmployee }: { userId: string | undefined; isEmployee: boolean }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const statusParam = searchParams.get('status') ?? '';
  const answeredParam = searchParams.get('answered') === '1';
  // "Assigned to me" is a button only an employee has, so it is only an employee's filter: for anybody else it would be a filter with no sign of it
  // on the page and nothing to take it off with.
  const mineOnly = isEmployee && searchParams.get('mine') === '1';
  const page = parsePage(searchParams.get('page'));
  const departmentId = searchParams.get('dept') ?? '';
  const assignedToUserId = searchParams.get('assignee') ?? '';
  const search = searchParams.get('q') ?? '';
  const createdFromIso = searchParams.get('from');
  const createdToIso = searchParams.get('to');
  const sortBy = searchParams.get('sort');
  const sortDir: 'asc' | 'desc' = searchParams.get('dir') === 'desc' ? 'desc' : 'asc';
  const createdFrom = useMemo(() => parseDate(createdFromIso), [createdFromIso]);
  const createdTo = useMemo(() => parseDate(createdToIso), [createdToIso]);
  const activeTab = tabOf(statusParam, answeredParam);

  /// Changes some of the filters. A change goes back to the first page unless it is itself a change of page.
  function update(changes: Record<string, string | null>, options?: { replace?: boolean }) {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        for (const [key, value] of Object.entries(changes)) {
          if (value === null || value === '') next.delete(key);
          else next.set(key, value);
        }
        if (!('page' in changes)) next.delete('page');
        return next;
      },
      { replace: options?.replace }
    );
  }

  // The search box is typed into freely and written to the address only once typing pauses: "Izv" is one request, not three. The spaces around
  // what was typed are not part of it.
  const [searchText, setSearchText] = useState(search);
  const settledSearch = useDebouncedValue(searchText.trim(), 300);
  const addressSearch = useRef(search);
  const latest = useRef({ search, update });
  latest.current = { search, update };
  useEffect(() => {
    if (settledSearch === latest.current.search) return;
    addressSearch.current = settledSearch;
    latest.current.update({ q: settledSearch }, { replace: true });
  }, [settledSearch]);
  useEffect(() => {
    // The address changed by itself (back button, a link): show it. A change this page just wrote must not overwrite what is being typed.
    if (search !== addressSearch.current) {
      addressSearch.current = search;
      setSearchText(search);
    }
  }, [search]);

  function selectTab(tab: TicketTab) {
    const p = tabToParams(tab);
    update({ status: p.status ?? null, answered: p.answered ?? null });
  }

  function sortByColumn(key: string) {
    if (sortBy === key) update({ dir: sortDir === 'asc' ? 'desc' : 'asc' });
    else update({ sort: key, dir: 'asc' });
  }

  // Nobody has a new ticket yet, and the filters for who has it are not offered there: they must not narrow the list either.
  const assignee = activeTab === 'new' ? undefined : mineOnly ? userId : assignedToUserId || undefined;

  const params: TicketListParams = {
    page,
    pageSize: PAGE_SIZE,
    status: statusParam || undefined,
    answeredOnly: activeTab === 'answered',
    departmentId: departmentId || undefined,
    assignedToUserId: assignee,
    search: search || undefined,
    createdFrom: createdFrom ? (createdFromIso ?? undefined) : undefined,
    createdTo: createdTo ? (createdToIso ?? undefined) : undefined,
    sortBy: sortBy ?? undefined,
    sortDir,
  };

  return {
    params,
    page,
    activeTab,
    mineOnly,
    departmentId,
    assignedToUserId,
    createdFrom,
    createdTo,
    sortBy,
    sortDir,
    searchText,
    setSearchText,
    update,
    selectTab,
    sortByColumn,
  };
}

export type TicketListFilters = ReturnType<typeof useTicketListFilters>;
