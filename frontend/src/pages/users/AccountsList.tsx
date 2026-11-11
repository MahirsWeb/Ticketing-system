import { Pager } from '../../components/Pager';
import { Tabs } from '../../components/Tabs';
import { ErrorText, Input } from '../../components/ui';
import { messageOf } from '../../lib/messageOf';
import { AccountsTable } from './AccountsTable';
import { PAGE_SIZE, useAccountList, type AccountTab } from './useAccountList';
import { useLanguage } from '../../i18n/LanguageContext';

/**
 * The accounts, a page at a time: the tabs (staff only for an administrator), the search, how many there are, the table and the pages.
 * The totals in the tabs come from the latest answer, even if it is for another tab, since they do not depend on what is searched for; the
 * rows, the count and the pages come only from the answer for what is chosen now, so that nothing of the tab that was left is shown under the
 * one that was chosen.
 */
export function AccountsList({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useLanguage();
  const { activeTab, setTab, search, setSearch, settledSearch, page, setPage, query } = useAccountList(isAdmin);
  const answer = query.isPlaceholderData ? undefined : query.data;
  const totals = query.data;
  const totalPages = Math.max(1, Math.ceil((answer?.totalCount ?? 0) / PAGE_SIZE));

  const message = query.isError ? null : answer ? (settledSearch ? t('accounts_noMatch') : t('accounts_empty')) : t('accounts_loading');
  const one = answer?.totalCount === 1;
  const count = answer ? t(settledSearch ? (one ? 'accounts_count1Match' : 'accounts_countNMatch') : one ? 'accounts_count1' : 'accounts_countN', { count: answer.totalCount }) : '';
  const tabs: { key: AccountTab; label: string }[] = [
    { key: 'clients', label: t('accounts_tabClients', { count: totals?.clientCount ?? '…' }) },
    { key: 'employees', label: t('accounts_tabStaff', { count: totals?.staffCount ?? '…' }) },
  ];

  return (
    <>
      {isAdmin && <Tabs label={t('accounts_title')} tabs={tabs} active={activeTab} onSelect={setTab} />}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Input
          className="max-w-sm"
          aria-label={t('accounts_searchAria')}
          placeholder={t('accounts_searchPlaceholder')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <span className="text-xs text-slate-600">{count}</span>
      </div>
      {query.isError && <ErrorText>{messageOf(query.error, t('accounts_loadFail'))}</ErrorText>}
      {!isAdmin && (
        <p className="text-xs text-slate-600">{t('accounts_staffHint', { button: t('ticket_new') })}</p>
      )}

      <AccountsTable rows={answer?.items ?? []} staff={activeTab === 'employees'} message={message} />
      {answer && <Pager page={page} totalPages={totalPages} onPage={setPage} />}
    </>
  );
}
