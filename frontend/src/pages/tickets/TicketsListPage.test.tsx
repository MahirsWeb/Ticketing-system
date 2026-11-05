import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation, useNavigate, useNavigationType } from 'react-router-dom';
import TicketsListPage from './TicketsListPage';
import { ticketsApi } from '../../api/tickets';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { TicketListItem } from '../../types';

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => {
      const navigate = actual.useNavigate();
      return (to: string) => {
        navigations.push(to);
        return navigate(to);
      };
    },
  };
});
vi.mock('../../api/tickets', () => ({ ticketsApi: { list: vi.fn() } }));
vi.mock('../../api/lookups', () => ({ lookupsApi: { departments: vi.fn() } }));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn() } }));
// A stand-in for the date picker, which has its own tests: it shows what it was given and lets a test choose or clear the dates.
vi.mock('../../components/DateRangePicker', () => ({
  DateRangePicker: ({ from, to, onChange, clearable }: { from: Date | null; to: Date | null; onChange: (f: Date | null, t: Date | null) => void; clearable?: boolean }) => (
    <div>
      <span data-testid="picker" data-clearable={String(!!clearable)}>{from ? from.toISOString() : 'no start'} / {to ? to.toISOString() : 'no end'}</span>
      <button onClick={() => onChange(new Date('2026-03-10T12:00:00.000Z'), new Date('2026-03-20T12:00:00.000Z'))}>pick March</button>
      <button onClick={() => onChange(null, null)}>clear the dates</button>
    </div>
  ),
}));

const list = vi.mocked(ticketsApi.list);
// Every address the page asks the router to go to by itself (a row that is clicked); links go through the router's own code and are not counted.
const navigations: string[] = [];

function ticket(number: string, title: string, extra: Partial<TicketListItem> = {}): TicketListItem {
  return {
    id: number, ticketNumber: number, title, status: 'Open', priority: 'Medium', clientId: 'c-' + number, clientName: 'Client', companyName: 'Acme',
    departmentName: 'Branch', assignedToName: null, createdAt: new Date(2026, 0, 5).toISOString(), dueDateUtc: null, hasUnreadResponse: false,
    ...extra,
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

const page = (items: TicketListItem[], totalCount = items.length) => ({ items, totalCount, page: 1, pageSize: 20 });
const searchesAsked = () => list.mock.calls.map((c) => c[0].search ?? null);

const person = (id: string, first: string, last: string, departmentId: string | null, departmentName: string | null) =>
  ({ id, firstName: first, lastName: last, departmentId, departmentName, role: 'Employee' }) as never;
const BRANCHES = [{ id: 'a', name: 'Branch A' }, { id: 'b', name: 'Branch B' }] as never;
const STAFF = [
  person('e1', 'Ana', 'Anić', 'b', 'Branch B'),
  person('e2', 'Ivo', 'Ivić', 'a', 'Branch A'),
  person('e3', 'Eli', 'Elić', 'a', 'Branch A'),
  person('e4', 'Zed', 'Zedić', null, null),
];

const as = (role: 'Admin' | 'Employee' | 'Client') =>
  useAuthStore.setState({ accessToken: 't', user: { id: 'u1', role, firstName: 'A', lastName: 'B' } as never });

/** Where the app is, as the address bar would say it, and how it got there. */
function Where() {
  const location = useLocation();
  const type = useNavigationType();
  return <div data-testid="where" data-type={type}>{location.pathname + location.search}</div>;
}
function Go({ to, children }: { to: string; children: string }) {
  const navigate = useNavigate();
  return <button onClick={() => navigate(to)}>{children}</button>;
}
const where = () => screen.getByTestId('where').textContent ?? '';
const addressParams = () => new URLSearchParams(where().split('?')[1] ?? '');
const lastAsked = () => list.mock.calls.at(-1)![0];

function renderPage(address = '/tickets') {
  return renderWithQuery(
    <MemoryRouter initialEntries={[address]}>
      <Where />
      <Go to="/tickets?q=abc">go to abc</Go>
      <Routes>
        <Route path="/tickets" element={<TicketsListPage />} />
        <Route path="/tickets/:id" element={<p>the ticket page</p>} />
        <Route path="/users/:id" element={<p>the user page</p>} />
      </Routes>
    </MemoryRouter>
  );
}
const newUser = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
const settle = () => act(async () => { await vi.advanceTimersByTimeAsync(400); });

describe('the tickets list', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    navigations.length = 0;
    list.mockReset().mockResolvedValue(page([]));
    vi.mocked(lookupsApi.departments).mockReset().mockResolvedValue(BRANCHES);
    vi.mocked(usersApi.list).mockReset().mockResolvedValue([]);
    as('Employee');
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
    list.mockReset().mockResolvedValueOnce(page([])).mockReturnValueOnce(first.promise as never).mockReturnValueOnce(second.promise as never);
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

  it('asks the server for exactly what the address says', async () => {
    renderPage('/tickets?status=Closed&dept=d1&assignee=e1&q=toner&sort=title&dir=desc&page=2&from=2026-01-01T00:00:00.000Z&to=2026-02-01T00:00:00.000Z');

    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(lastAsked()).toEqual({
      page: 2,
      pageSize: 20,
      status: 'Closed',
      answeredOnly: false,
      departmentId: 'd1',
      assignedToUserId: 'e1',
      search: 'toner',
      createdFrom: '2026-01-01T00:00:00.000Z',
      createdTo: '2026-02-01T00:00:00.000Z',
      sortBy: 'title',
      sortDir: 'desc',
    });
  });

  it('asks for the tickets of the person who is signed in when "mine" is chosen, except among the new ones, which nobody has yet', async () => {
    const mine = renderPage('/tickets?mine=1&status=Open,InProgress,Resolved');
    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(lastAsked().assignedToUserId).toBe('u1');
    mine.unmount();

    list.mockClear();
    renderPage('/tickets?mine=1&status=New');
    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(lastAsked().assignedToUserId).toBeUndefined();
  });

  it('does not ask for the list of staff when the person is a client, who has no use for it', async () => {
    as('Client');
    list.mockResolvedValue(page([ticket('100001', 'Printer')]));

    renderPage();

    expect(await screen.findByText('Printer')).toBeInTheDocument();
    expect(usersApi.list).not.toHaveBeenCalled();
  });

  it('asks for the list of staff when the person is staff, for the "assigned to" filter', async () => {
    renderPage();

    await waitFor(() => expect(usersApi.list).toHaveBeenCalledWith({ role: 'Employee' }));
  });

  it('asks only for the tickets that have been answered on the "answered" tab', async () => {
    renderPage('/tickets?answered=1&status=Open,InProgress,Resolved');

    await waitFor(() => expect(list).toHaveBeenCalled());
    expect(lastAsked().answeredOnly).toBe(true);
  });

  it('says so, instead of failing silently, when the list cannot be loaded', async () => {
    list.mockRejectedValue(new Error('network'));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('could not be loaded');
  });

  describe('the groups (tabs)', () => {
    it('are All, New, Opened, Answered and Closed, with All chosen when the address says nothing', async () => {
      renderPage();

      const tabs = screen.getAllByRole('tab');
      expect(tabs.map((t) => t.textContent)).toEqual(['All', 'New', 'Opened', 'Answered', 'Closed']);
      expect(tabs.filter((t) => t.getAttribute('aria-selected') === 'true').map((t) => t.textContent)).toEqual(['All']);
    });

    it.each([
      ['New', 'New', null],
      ['Opened', 'Open,InProgress,Resolved', null],
      ['Answered', 'Open,InProgress,Resolved', '1'],
      ['Closed', 'Closed', null],
    ])('write %s in the address and go back to the first page', async (label, status, answered) => {
      const user = newUser();
      renderPage('/tickets?page=3');

      await user.click(screen.getByRole('tab', { name: label }));

      expect(addressParams().get('status')).toBe(status);
      expect(addressParams().get('answered')).toBe(answered);
      expect(addressParams().has('page')).toBe(false);
      expect(screen.getByRole('tab', { name: label })).toHaveAttribute('aria-selected', 'true');
    });

    it('All takes the group out of the address', async () => {
      const user = newUser();
      renderPage('/tickets?status=Open,InProgress,Resolved&answered=1');

      await user.click(screen.getByRole('tab', { name: 'All' }));

      expect(addressParams().has('status')).toBe(false);
      expect(addressParams().has('answered')).toBe(false);
    });

    it.each([
      ['/tickets?status=New', 'New'],
      ['/tickets?status=Closed', 'Closed'],
      ['/tickets?status=Open,InProgress,Resolved', 'Opened'],
      ['/tickets?status=Open,InProgress,Resolved&answered=1', 'Answered'],
      ['/tickets?answered=1', 'Answered'],
    ])('are chosen from the address %s: %s', async (address, label) => {
      renderPage(address);

      expect(screen.getByRole('tab', { name: label })).toHaveAttribute('aria-selected', 'true');
      expect(screen.getAllByRole('tab').filter((t) => t.getAttribute('aria-selected') === 'true')).toHaveLength(1);
    });
  });

  describe('what each kind of person sees', () => {
    it('shows a client "My Tickets", a way to submit one, and neither the client nor the priority of a ticket', async () => {
      as('Client');
      list.mockResolvedValue(page([ticket('100001', 'Printer')]));
      renderPage();
      await screen.findByText('Printer');

      expect(screen.getByRole('heading', { name: 'My Tickets' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Submit a ticket' })).toHaveAttribute('href', '/tickets/new');
      expect(screen.queryByRole('link', { name: 'New ticket' })).not.toBeInTheDocument();
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['#', 'Title', 'Created', 'Assigned to']);
      expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Assigned employee')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Assigned to me' })).not.toBeInTheDocument();
    });

    it('shows an employee the staff view: a way to make a ticket, the client and priority columns, the "assigned to" filter and "Assigned to me", but no branch', async () => {
      list.mockResolvedValue(page([ticket('100001', 'Printer')]));
      renderPage();
      await screen.findByText('Printer');

      expect(screen.getByRole('heading', { name: 'Tickets' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'New ticket' })).toHaveAttribute('href', '/tickets/new-on-behalf');
      expect(screen.queryByRole('link', { name: 'Submit a ticket' })).not.toBeInTheDocument();
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['#', 'Title', 'Created', 'Client', 'Priority', 'Assigned to']);
      expect(screen.getByLabelText('Assigned employee')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Assigned to me' })).toBeInTheDocument();
      expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument();
      expect(lookupsApi.departments).not.toHaveBeenCalled();
    });

    it('shows an administrator the branches and the "assigned to" filter, but not "Assigned to me"', async () => {
      as('Admin');
      list.mockResolvedValue(page([ticket('100001', 'Printer')]));
      renderPage();
      await screen.findByText('Printer');

      expect(await screen.findByRole('option', { name: 'Branch A' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'New ticket' })).toBeInTheDocument();
      expect(screen.getByLabelText('Assigned employee')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Assigned to me' })).not.toBeInTheDocument();
    });
  });

  describe('the filters', () => {
    it('lets an administrator choose a branch: it goes into the address, the employee goes out of it, and the list starts again at page 1', async () => {
      as('Admin');
      const user = newUser();
      renderPage('/tickets?assignee=e2&page=3');
      await screen.findByRole('option', { name: 'Branch B' });

      await user.selectOptions(screen.getByLabelText('Branch'), 'b');

      expect(addressParams().get('dept')).toBe('b');
      expect(addressParams().has('assignee')).toBe(false);
      expect(addressParams().has('page')).toBe(false);
    });

    it('lets them go back to all branches', async () => {
      as('Admin');
      const user = newUser();
      renderPage('/tickets?dept=b');
      await screen.findByRole('option', { name: 'Branch B' });

      await user.selectOptions(screen.getByLabelText('Branch'), '');

      expect(addressParams().has('dept')).toBe(false);
    });

    it('groups the employees by branch, in the order of the branch names, and puts those with no branch last', async () => {
      vi.mocked(usersApi.list).mockResolvedValue(STAFF);
      renderPage();
      const select = screen.getByLabelText('Assigned employee');
      await within(select).findByRole('group', { name: 'Branch A' });

      const groups = within(select).getAllByRole('group');
      expect(groups.map((g) => g.getAttribute('label'))).toEqual(['Branch A', 'Branch B', 'No branch']);
      expect(within(groups[0]).getAllByRole('option').map((o) => o.textContent)).toEqual(['Ivo Ivić', 'Eli Elić']);
      expect(within(groups[2]).getAllByRole('option').map((o) => o.textContent)).toEqual(['Zed Zedić']);
      expect(within(select).getAllByRole('option')[0]).toHaveTextContent('All employees');
    });

    it('shows only the employees of the chosen branch', async () => {
      as('Admin');
      vi.mocked(usersApi.list).mockResolvedValue(STAFF);
      renderPage('/tickets?dept=a');
      const select = screen.getByLabelText('Assigned employee');
      await within(select).findByRole('group', { name: 'Branch A' });

      expect(within(select).getAllByRole('group').map((g) => g.getAttribute('label'))).toEqual(['Branch A']);
    });

    it('puts the chosen employee in the address, and takes it out when "All employees" is chosen', async () => {
      const user = newUser();
      vi.mocked(usersApi.list).mockResolvedValue(STAFF);
      renderPage('/tickets?page=2');
      await screen.findByRole('option', { name: 'Ivo Ivić' });

      await user.selectOptions(screen.getByLabelText('Assigned employee'), 'e2');
      expect(addressParams().get('assignee')).toBe('e2');
      expect(addressParams().has('page')).toBe(false);
      expect(lastAsked().assignedToUserId).toBe('e2');

      await user.selectOptions(screen.getByLabelText('Assigned employee'), '');
      expect(addressParams().has('assignee')).toBe(false);
    });

    it('lets an employee ask for "Assigned to me" and out of it again; asking for it hides the employee filter, which it replaces', async () => {
      const user = newUser();
      renderPage('/tickets?assignee=e2');
      const button = screen.getByRole('button', { name: 'Assigned to me' });
      expect(button).toHaveAttribute('aria-pressed', 'false');

      await user.click(button);
      expect(addressParams().get('mine')).toBe('1');
      expect(button).toHaveAttribute('aria-pressed', 'true');
      expect(screen.queryByLabelText('Assigned employee')).not.toBeInTheDocument();
      expect(lastAsked().assignedToUserId).toBe('u1');

      await user.click(button);
      expect(addressParams().has('mine')).toBe(false);
      expect(button).toHaveAttribute('aria-pressed', 'false');
      expect(screen.getByLabelText('Assigned employee')).toBeInTheDocument();
    });

    it('has neither "Assigned to me" nor the employee filter among the new tickets, which nobody has yet', async () => {
      renderPage('/tickets?status=New');

      expect(screen.queryByRole('button', { name: 'Assigned to me' })).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Assigned employee')).not.toBeInTheDocument();
    });

    it('puts the dates that were picked in the address as the start and the end, and takes them out when cleared', async () => {
      const user = newUser();
      renderPage('/tickets?page=2');

      await user.click(screen.getByRole('button', { name: 'pick March' }));
      expect(addressParams().get('from')).toBe('2026-03-10T12:00:00.000Z');
      expect(addressParams().get('to')).toBe('2026-03-20T12:00:00.000Z');
      expect(addressParams().has('page')).toBe(false);
      expect(screen.getByTestId('picker')).toHaveTextContent('2026-03-10T12:00:00.000Z / 2026-03-20T12:00:00.000Z');
      expect(screen.getByTestId('picker')).toHaveAttribute('data-clearable', 'true');

      await user.click(screen.getByRole('button', { name: 'clear the dates' }));
      expect(addressParams().has('from')).toBe(false);
      expect(addressParams().has('to')).toBe(false);
    });

    it('says which period the list is for', async () => {
      const user = newUser();
      renderPage();
      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('all time'));

      await user.click(screen.getByRole('button', { name: 'pick March' }));
      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('from 10.03.2026 to 20.03.2026'));
    });

    it('says "to today" when the period has a start but no end', async () => {
      renderPage('/tickets?from=2026-03-10T12:00:00.000Z');

      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('from 10.03.2026 to today'));
    });
  });

  describe('the search box', () => {
    it('shows what the address says, and shows a new address the page did not write itself (the back button, a link)', async () => {
      const user = newUser();
      renderPage('/tickets?q=toner');
      const box = screen.getByLabelText('Search tickets by number or title');
      expect(box).toHaveValue('toner');

      await user.click(screen.getByRole('button', { name: 'go to abc' }));

      expect(box).toHaveValue('abc');
      await waitFor(() => expect(searchesAsked().at(-1)).toBe('abc'));
    });

    it('writes what was typed into the address by replacing the entry, so that the back button does not step through the typing, and starts at page 1', async () => {
      const user = newUser();
      renderPage('/tickets?page=2');

      await user.type(screen.getByLabelText('Search tickets by number or title'), 'Izv');
      await settle();

      expect(addressParams().get('q')).toBe('Izv');
      expect(addressParams().has('page')).toBe(false);
      expect(screen.getByTestId('where')).toHaveAttribute('data-type', 'REPLACE');
    });

    it('does not touch the address, or the list, while the typing goes on', async () => {
      const user = newUser();
      renderPage();
      await waitFor(() => expect(list).toHaveBeenCalledTimes(1));

      await user.type(screen.getByLabelText('Search tickets by number or title'), 'Iz');

      expect(addressParams().has('q')).toBe(false);
      expect(list).toHaveBeenCalledTimes(1);
    });
  });

  describe('sorting', () => {
    it('sorts by a column when its heading is chosen, ascending first, and the other way round when it is chosen again', async () => {
      const user = newUser();
      renderPage('/tickets?page=2');
      await screen.findByRole('table');
      const heading = () => screen.getByRole('columnheader', { name: /Title/ });
      expect(heading()).toHaveAttribute('aria-sort', 'none');

      await user.click(within(heading()).getByRole('button'));
      expect(addressParams().get('sort')).toBe('title');
      expect(addressParams().get('dir')).toBe('asc');
      expect(addressParams().has('page')).toBe(false);
      expect(heading()).toHaveAttribute('aria-sort', 'ascending');
      expect(heading()).toHaveTextContent('Title▲');
      expect(lastAsked()).toMatchObject({ sortBy: 'title', sortDir: 'asc' });

      await user.click(within(heading()).getByRole('button'));
      expect(addressParams().get('dir')).toBe('desc');
      expect(heading()).toHaveAttribute('aria-sort', 'descending');
      expect(heading()).toHaveTextContent('Title▼');

      await user.click(within(heading()).getByRole('button'));
      expect(addressParams().get('dir')).toBe('asc');
    });

    it('starts again from ascending when another column is chosen', async () => {
      const user = newUser();
      renderPage('/tickets?sort=title&dir=desc');
      await screen.findByRole('table');

      await user.click(within(screen.getByRole('columnheader', { name: /Created/ })).getByRole('button'));

      expect(addressParams().get('sort')).toBe('createdAt');
      expect(addressParams().get('dir')).toBe('asc');
      expect(screen.getByRole('columnheader', { name: /Title/ })).toHaveAttribute('aria-sort', 'none');
    });

    it('asks for ascending when the address names a column and no direction, and for no column when it names none', async () => {
      renderPage('/tickets?sort=title');
      await waitFor(() => expect(list).toHaveBeenCalled());
      expect(lastAsked()).toMatchObject({ sortBy: 'title', sortDir: 'asc' });
    });

    it('sorts each column by the name the server knows it by', async () => {
      const user = newUser();
      renderPage();
      await screen.findByRole('table');

      const chosen: (string | null)[] = [];
      for (const name of ['#', 'Title', 'Created', 'Priority']) {
        await user.click(within(screen.getByRole('columnheader', { name: new RegExp(`^${name === '#' ? '#' : name}`) })).getByRole('button'));
        chosen.push(addressParams().get('sort'));
      }

      expect(chosen).toEqual(['ticketNumber', 'title', 'createdAt', 'priority']);
    });

    it('can sort by number, title, created and (for staff) priority, and by nothing else', async () => {
      renderPage();
      await screen.findByRole('table');

      const sortable = screen.getAllByRole('columnheader').filter((h) => within(h).queryByRole('button'));
      expect(sortable.map((h) => h.textContent)).toEqual(['#', 'Title', 'Created', 'Priority']);
    });

    it('does not offer sorting by priority to a client, who does not see it', async () => {
      as('Client');
      renderPage();
      await screen.findByRole('table');

      const sortable = screen.getAllByRole('columnheader').filter((h) => within(h).queryByRole('button'));
      expect(sortable.map((h) => h.textContent)).toEqual(['#', 'Title', 'Created']);
    });
  });

  describe('the rows', () => {
    const rows = () => within(screen.getAllByRole('rowgroup')[1]).getAllByRole('row');

    it('show the number, the title, when it was made, who made it, how urgent it is and who has it', async () => {
      list.mockResolvedValue(page([
        ticket('100001', 'Printer', { clientName: 'Mia Marić', priority: 'High', assignedToName: 'Ivo Ivić' }),
        ticket('100002', 'Monitor', { assignedToName: null }),
      ]));
      renderPage();
      await screen.findByText('Printer');

      const first = within(rows()[0]);
      expect(first.getByRole('link', { name: '#100001' })).toHaveAttribute('href', '/tickets/100001');
      expect(first.getByText('Printer')).toBeInTheDocument();
      expect(first.getByText('05.01.2026 00:00')).toBeInTheDocument();
      expect(first.getByRole('link', { name: 'Mia Marić' })).toHaveAttribute('href', '/users/c-100001');
      expect(first.getByText('High')).toBeInTheDocument();
      expect(first.getByText('Ivo Ivić')).toBeInTheDocument();
      expect(within(rows()[1]).getByText('—')).toBeInTheDocument();
    });

    it('leave out the client and the priority for a client', async () => {
      as('Client');
      list.mockResolvedValue(page([ticket('100001', 'Printer', { priority: 'High' })]));
      renderPage();
      await screen.findByText('Printer');

      expect(within(rows()[0]).queryByRole('link', { name: 'Client' })).not.toBeInTheDocument();
      expect(within(rows()[0]).queryByText('High')).not.toBeInTheDocument();
      expect(within(rows()[0]).getAllByRole('cell')).toHaveLength(4);
    });

    it('open the ticket when the row is clicked', async () => {
      const user = newUser();
      list.mockResolvedValue(page([ticket('100001', 'Printer')]));
      renderPage();

      await user.click(await screen.findByText('Printer'));

      expect(await screen.findByText('the ticket page')).toBeInTheDocument();
      expect(where()).toBe('/tickets/100001');
    });

    it('open the client, and not the ticket, when the name of the client is clicked', async () => {
      const user = newUser();
      list.mockResolvedValue(page([ticket('100001', 'Printer', { clientName: 'Mia Marić' })]));
      renderPage();

      await user.click(await screen.findByRole('link', { name: 'Mia Marić' }));

      expect(await screen.findByText('the user page')).toBeInTheDocument();
      expect(where()).toBe('/users/c-100001');
      expect(navigations).toEqual([]);
    });

    it('open the ticket once from the number, through the link', async () => {
      const user = newUser();
      list.mockResolvedValue(page([ticket('100001', 'Printer')]));
      renderPage();

      await user.click(await screen.findByRole('link', { name: '#100001' }));

      expect(await screen.findByText('the ticket page')).toBeInTheDocument();
      expect(where()).toBe('/tickets/100001');
      expect(navigations).toEqual([]);
    });

    it('is the row that opens the ticket when the row, and not a link in it, is clicked', async () => {
      const user = newUser();
      list.mockResolvedValue(page([ticket('100001', 'Printer')]));
      renderPage();

      await user.click(await screen.findByText('Printer'));

      expect(navigations).toEqual(['/tickets/100001']);
    });

    it('say "No tickets found." across the whole width of the table when there are none', async () => {
      renderPage();

      const cell = (await screen.findByText('No tickets found.')).closest('td')!;
      expect(cell).toHaveAttribute('colspan', '6');
    });

    it('say it across the narrower table of a client too', async () => {
      as('Client');
      renderPage();

      expect((await screen.findByText('No tickets found.')).closest('td')).toHaveAttribute('colspan', '4');
    });

    it('wait behind a spinner for the first answer, then show the table', async () => {
      const answer = deferred<ReturnType<typeof page>>();
      list.mockReturnValue(answer.promise as never);
      renderPage();

      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      expect(screen.queryByText('No tickets found.')).not.toBeInTheDocument();

      answer.resolve(page([ticket('100001', 'Printer')]));
      expect(await screen.findByRole('table')).toBeInTheDocument();
    });
  });

  describe('the count and the pages', () => {
    it('counts the tickets, in the singular for one', async () => {
      list.mockResolvedValue(page([ticket('100001', 'Printer')]));
      renderPage();

      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('1 ticket · all time'));
    });

    it('counts all the tickets the filters match, not only the ones on the page', async () => {
      list.mockResolvedValue(page([ticket('100001', 'Printer')], 45));
      renderPage();

      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('45 tickets · all time'));
    });

    it('has no pages to go between when everything fits on one', async () => {
      list.mockResolvedValue(page([ticket('100001', 'Printer')], 20));
      renderPage();
      await screen.findByText('Printer');

      expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument();
    });

    it('goes forward and back by pages of 20, and cannot go past either end', async () => {
      const user = newUser();
      list.mockResolvedValue(page([ticket('100001', 'Printer')], 45));
      renderPage();
      await screen.findByText('Page 1 of 3');
      expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();

      await user.click(screen.getByRole('button', { name: 'Next' }));
      expect(addressParams().get('page')).toBe('2');
      expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument();
      expect(lastAsked().page).toBe(2);

      await user.click(screen.getByRole('button', { name: 'Next' }));
      expect(await screen.findByText('Page 3 of 3')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();

      await user.click(screen.getByRole('button', { name: 'Previous' }));
      expect(addressParams().get('page')).toBe('2');
    });

    it('keeps the other filters when it changes the page', async () => {
      const user = newUser();
      list.mockResolvedValue(page([ticket('100001', 'Printer')], 45));
      renderPage('/tickets?status=Closed&q=toner');
      await screen.findByText('Page 1 of 3');

      await user.click(screen.getByRole('button', { name: 'Next' }));

      expect(addressParams().get('status')).toBe('Closed');
      expect(addressParams().get('q')).toBe('toner');
    });

    it('takes a page that is not a number, or is zero or less, for the first', async () => {
      renderPage('/tickets?page=abc');
      await waitFor(() => expect(list).toHaveBeenCalled());
      expect(lastAsked().page).toBe(1);
    });
  });

  describe('what it used to get wrong', () => {
    it('does not fall over, and does not ask for a period, when a date in the address is not a date', async () => {
      renderPage('/tickets?from=nonsense&to=2026-13-45');

      expect(await screen.findByRole('table')).toBeInTheDocument();
      expect(lastAsked().createdFrom).toBeUndefined();
      expect(lastAsked().createdTo).toBeUndefined();
      expect(screen.getByRole('status')).toHaveTextContent('all time');
    });

    it('says which tickets the period is for when only an end is in the address, instead of "all time" for a list that is cut off', async () => {
      renderPage('/tickets?to=2026-03-20T12:00:00.000Z');

      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('up to 20.03.2026'));
      expect(screen.getByRole('status')).not.toHaveTextContent('all time');
    });

    it('shows the way back when the address is for a page after the last one, instead of "No tickets found" with nothing to press', async () => {
      const user = newUser();
      list.mockImplementation((async (p: { page?: number }) => (p.page === 2 ? page([ticket('100001', 'Printer')], 25) : page([], 25))) as never);
      renderPage('/tickets?page=9');

      expect(await screen.findByText('There are no tickets on this page.')).toBeInTheDocument();
      expect(screen.queryByText('No tickets found.')).not.toBeInTheDocument();
      expect(screen.getByText('Page 9 of 2')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Previous' }));
      expect(addressParams().get('page')).toBe('2');
      expect(await screen.findByText('Printer')).toBeInTheDocument();
    });

    it('shows the way back to the first page when there is only one page of tickets and the address is for a later one', async () => {
      const user = newUser();
      list.mockImplementation((async (p: { page?: number }) => (p.page === 1 || p.page === undefined ? page([ticket('100001', 'Printer')], 3) : page([], 3))) as never);
      renderPage('/tickets?page=5');

      const previous = await screen.findByRole('button', { name: 'Previous' });
      await user.click(previous);

      expect(addressParams().get('page') ?? '1').toBe('1');
      expect(await screen.findByText('Printer')).toBeInTheDocument();
    });

    it('still says "No tickets found." when the filters match nothing at all', async () => {
      renderPage('/tickets?q=zzz');

      expect(await screen.findByText('No tickets found.')).toBeInTheDocument();
    });

    it('does not filter by "mine" for an administrator or a client, who have no such button and could not take it off again', async () => {
      as('Admin');
      const admin = renderPage('/tickets?mine=1');
      await waitFor(() => expect(list).toHaveBeenCalled());
      expect(lastAsked().assignedToUserId).toBeUndefined();
      expect(screen.getByLabelText('Assigned employee')).toBeInTheDocument();
      admin.unmount();

      as('Client');
      list.mockClear();
      renderPage('/tickets?mine=1');
      await waitFor(() => expect(list).toHaveBeenCalled());
      expect(lastAsked().assignedToUserId).toBeUndefined();
    });

    it('does not say "0 tickets" while the list is still being fetched, or when it could not be fetched', async () => {
      const answer = deferred<ReturnType<typeof page>>();
      list.mockReturnValueOnce(answer.promise as never);
      const first = renderPage();
      expect(screen.getByRole('status')).toHaveTextContent('Loading tickets…');
      expect(screen.getByRole('status')).not.toHaveTextContent('0 tickets');
      first.unmount();

      list.mockRejectedValue(new Error('network'));
      renderPage();
      await screen.findByRole('alert');
      expect(screen.getByRole('status')).not.toHaveTextContent('0 tickets');
      expect(screen.getByRole('status')).not.toHaveTextContent('Loading');
    });

    it('asks for what was typed without the spaces around it, and not at all when it was only spaces', async () => {
      const user = newUser();
      renderPage();
      const box = screen.getByLabelText('Search tickets by number or title');

      await user.type(box, '  Izv ');
      await settle();
      expect(searchesAsked().at(-1)).toBe('Izv');
      expect(addressParams().get('q')).toBe('Izv');
      expect(box).toHaveValue('  Izv ');

      await user.clear(box);
      await user.type(box, '   ');
      await settle();
      expect(addressParams().has('q')).toBe(false);
      expect(searchesAsked().at(-1)).toBeNull();
    });

    it('takes a page that is not a whole number for the whole number below it', async () => {
      renderPage('/tickets?page=2.5');

      await waitFor(() => expect(list).toHaveBeenCalled());
      expect(lastAsked().page).toBe(2);
    });

    it('does not choose "All" when the address asks for a status that is none of the groups, because the list is not all of them', async () => {
      renderPage('/tickets?status=Resolved');

      await waitFor(() => expect(list).toHaveBeenCalled());
      expect(lastAsked().status).toBe('Resolved');
      expect(screen.getAllByRole('tab').filter((t) => t.getAttribute('aria-selected') === 'true')).toHaveLength(0);
    });

    it('does not narrow the new tickets by an employee, which cannot be chosen there and which no new ticket has, so that the list is empty for no visible reason', async () => {
      const user = newUser();
      renderPage('/tickets?assignee=e2');
      await waitFor(() => expect(lastAsked().assignedToUserId).toBe('e2'));

      await user.click(screen.getByRole('tab', { name: 'New' }));

      await waitFor(() => expect(lastAsked().status).toBe('New'));
      expect(lastAsked().assignedToUserId).toBeUndefined();
    });

    it('still narrows the new tickets by branch, which can be chosen there', async () => {
      as('Admin');
      renderPage('/tickets?status=New&dept=b');

      await waitFor(() => expect(list).toHaveBeenCalled());
      expect(lastAsked()).toMatchObject({ status: 'New', departmentId: 'b' });
    });

    it('says when the branches could not be loaded, since the filter would otherwise offer nothing and give no reason', async () => {
      as('Admin');
      vi.mocked(lookupsApi.departments).mockRejectedValue(new Error('network'));
      renderPage();

      expect(await screen.findByText('The list of branches could not be loaded.')).toBeInTheDocument();
    });

    it('says when the employees could not be loaded', async () => {
      vi.mocked(usersApi.list).mockRejectedValue(new Error('network'));
      renderPage();

      expect(await screen.findByText('The list of employees could not be loaded.')).toBeInTheDocument();
    });

    it('does not say so when they could', async () => {
      as('Admin');
      vi.mocked(usersApi.list).mockResolvedValue(STAFF);
      renderPage();
      await screen.findByRole('option', { name: 'Ivo Ivić' });

      expect(screen.queryByText(/could not be loaded/)).not.toBeInTheDocument();
    });

    it('shows that the filter is in use when it is for an employee or a branch that is not in the list, instead of "All employees" over a filtered list', async () => {
      as('Admin');
      vi.mocked(usersApi.list).mockResolvedValue(STAFF);
      renderPage('/tickets?assignee=gone&dept=lost');
      await screen.findByRole('option', { name: 'Branch A' });
      await screen.findByRole('option', { name: 'Selected employee' });

      expect(screen.getByLabelText('Assigned employee')).toHaveDisplayValue('Selected employee');
      expect(screen.getByLabelText('Branch')).toHaveDisplayValue('Selected branch');
    });

    it('shows it too for an employee of another branch than the one chosen, who is not among the ones offered', async () => {
      as('Admin');
      vi.mocked(usersApi.list).mockResolvedValue(STAFF);
      renderPage('/tickets?dept=a&assignee=e1');
      await screen.findByRole('option', { name: 'Selected employee' });

      expect(screen.getByLabelText('Assigned employee')).toHaveDisplayValue('Selected employee');
    });

    it('does not show that while the lists are still on their way', async () => {
      as('Admin');
      vi.mocked(usersApi.list).mockReturnValue(new Promise(() => {}) as never);
      vi.mocked(lookupsApi.departments).mockReturnValue(new Promise(() => {}) as never);
      renderPage('/tickets?assignee=e2&dept=a');

      expect(screen.queryByRole('option', { name: 'Selected employee' })).not.toBeInTheDocument();
      expect(screen.queryByRole('option', { name: 'Selected branch' })).not.toBeInTheDocument();
    });

    it('does not show it for an employee or a branch that is in the list', async () => {
      as('Admin');
      vi.mocked(usersApi.list).mockResolvedValue(STAFF);
      renderPage('/tickets?assignee=e2&dept=a');
      await screen.findByRole('option', { name: 'Ivo Ivić' });

      expect(screen.getByLabelText('Assigned employee')).toHaveDisplayValue('Ivo Ivić');
      expect(screen.getByLabelText('Branch')).toHaveDisplayValue('Branch A');
      expect(screen.queryByRole('option', { name: 'Selected employee' })).not.toBeInTheDocument();
      expect(screen.queryByRole('option', { name: 'Selected branch' })).not.toBeInTheDocument();
    });
  });
});
