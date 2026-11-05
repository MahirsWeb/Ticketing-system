import { Link } from 'react-router-dom';
import { useTicketList } from '../../api/ticketQueries';
import { Button, Card, ErrorText, Spinner } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { PAGE_SIZE, rangeLabel, TABS } from './ticketListAddress';
import { TicketFilterBar } from './TicketFilterBar';
import { Pager } from '../../components/Pager';
import { Tabs } from '../../components/Tabs';
import { TicketsTable } from './TicketsTable';
import { useTicketListFilters } from './useTicketListFilters';

export default function TicketsListPage() {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const isEmployee = user?.role === 'Employee';
  const isStaff = isAdmin || isEmployee;
  const filters = useTicketListFilters({ userId: user?.id, isEmployee });

  // Each combination of filters is a list of its own: the answer to an earlier one can never be shown as the answer to the current one.
  const { data: loaded, isPending, isError } = useTicketList(filters.params);
  const totalCount = loaded?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  // A count is only said when it is known: not while the list is on its way, and not when it could not be had.
  const countText = isPending
    ? 'Loading tickets…'
    : isError
      ? ''
      : `${totalCount} ticket${totalCount === 1 ? '' : 's'} · ${rangeLabel(filters.createdFrom, filters.createdTo)}`;

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-xl font-bold text-slate-900">{user?.role === 'Client' ? 'My Tickets' : 'Tickets'}</h1>
        {user?.role === 'Client' && (
          <Link to="/tickets/new">
            <Button>Submit a ticket</Button>
          </Link>
        )}
        {isStaff && (
          <Link to="/tickets/new-on-behalf">
            <Button>New ticket</Button>
          </Link>
        )}
      </div>

      <Tabs label="Ticket groups" tabs={TABS} active={filters.activeTab} onSelect={filters.selectTab} className="mb-4" />
      <TicketFilterBar filters={filters} isAdmin={isAdmin} isEmployee={isEmployee} />

      {isError && <ErrorText>The tickets could not be loaded. Try again in a moment.</ErrorText>}
      <p className="mb-2 px-1 text-xs text-slate-500" role="status">
        {countText}
      </p>

      <Card className="overflow-hidden">
        {isPending ? (
          <div className="flex justify-center py-10">
            <Spinner className="h-6 w-6 text-blue-700" />
          </div>
        ) : (
          <TicketsTable
            items={loaded?.items ?? []}
            isStaff={isStaff}
            sortBy={filters.sortBy}
            sortDir={filters.sortDir}
            onSort={filters.sortByColumn}
            // More tickets than are on this page, and none shown: the address is for a page after the last, not for a list with nothing in it.
            emptyText={totalCount > 0 ? 'There are no tickets on this page.' : 'No tickets found.'}
          />
        )}
      </Card>

      <Pager page={filters.page} totalPages={totalPages} onPage={(p) => filters.update({ page: String(p) })} />
    </div>
  );
}
