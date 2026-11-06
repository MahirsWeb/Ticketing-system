import { Pager } from '../../components/Pager';
import { Tabs } from '../../components/Tabs';
import { ErrorText, Input } from '../../components/ui';
import { messageOf } from '../../lib/messageOf';
import { AccountsTable } from './AccountsTable';
import { PAGE_SIZE, useAccountList, type AccountTab } from './useAccountList';

/**
 * The accounts, a page at a time: the tabs (staff only for an administrator), the search, how many there are, the table and the pages.
 * The totals in the tabs come from the latest answer, even if it is for another tab, since they do not depend on what is searched for; the
 * rows, the count and the pages come only from the answer for what is chosen now, so that nothing of the tab that was left is shown under the
 * one that was chosen.
 */
export function AccountsList({ isAdmin }: { isAdmin: boolean }) {
  const { activeTab, setTab, search, setSearch, settledSearch, page, setPage, query } = useAccountList(isAdmin);
  const answer = query.isPlaceholderData ? undefined : query.data;
  const totals = query.data;
  const totalPages = Math.max(1, Math.ceil((answer?.totalCount ?? 0) / PAGE_SIZE));

  const message = query.isError ? null : answer ? (settledSearch ? 'No accounts match your search.' : 'No accounts here yet.') : 'Loading accounts…';
  const count = answer ? `${answer.totalCount} ${answer.totalCount === 1 ? 'account' : 'accounts'}${settledSearch ? ' match' : ''}` : '';
  const tabs: { key: AccountTab; label: string }[] = [
    { key: 'clients', label: `Clients (${totals?.clientCount ?? '…'})` },
    { key: 'employees', label: `Employees & Admins (${totals?.staffCount ?? '…'})` },
  ];

  return (
    <>
      {isAdmin && <Tabs label="Accounts" tabs={tabs} active={activeTab} onSelect={setTab} />}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Input
          className="max-w-sm"
          aria-label="Search accounts"
          placeholder="Search by name or company — or type a full email"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <span className="text-xs text-slate-600">{count}</span>
      </div>
      {query.isError && <ErrorText>{messageOf(query.error, 'Could not load the accounts.')}</ErrorText>}
      {!isAdmin && (
        <p className="text-xs text-slate-600">
          You see the clients who have tickets in your branch. To open a ticket for any other client, use "New ticket on behalf" and type their full email address.
        </p>
      )}

      <AccountsTable rows={answer?.items ?? []} staff={activeTab === 'employees'} message={message} />
      {answer && <Pager page={page} totalPages={totalPages} onPage={setPage} />}
    </>
  );
}
