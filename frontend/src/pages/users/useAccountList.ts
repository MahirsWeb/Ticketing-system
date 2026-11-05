import { useState } from 'react';
import { useUserPage } from '../../api/userQueries';
import { useDebouncedValue } from '../../lib/useDebouncedValue';

export const PAGE_SIZE = 25;

export type AccountTab = 'clients' | 'employees';

/**
 * What the list of accounts is for: the tab, what is searched for, and the page. Only an administrator sees the staff; for anybody else the
 * list is the clients. What is typed is asked for once typing pauses, without the spaces around it. The page belongs to the tab and the search it
 * was chosen in, so another tab or search starts again from the first page without first asking for a page that it may not have.
 */
export function useAccountList(canSeeStaff: boolean) {
  const [tab, setTab] = useState<AccountTab>('clients');
  const activeTab = canSeeStaff ? tab : 'clients';
  const [search, setSearch] = useState('');
  const settledSearch = useDebouncedValue(search.trim(), 300);

  const key = `${activeTab}|${settledSearch}`;
  const [paging, setPaging] = useState({ key, page: 1 });
  // Coming back to a tab or a search that was left does not bring back the page it was on: it starts again from the first.
  if (paging.key !== key) setPaging({ key, page: 1 });
  const page = paging.key === key ? paging.page : 1;

  const query = useUserPage({ group: activeTab === 'clients' ? 'clients' : 'staff', search: settledSearch || undefined, page, pageSize: PAGE_SIZE });

  return { activeTab, setTab, search, setSearch, settledSearch, page, setPage: (next: number) => setPaging({ key, page: next }), query };
}
