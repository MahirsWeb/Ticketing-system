import { afterAll, afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import UsersPage from './UsersPage';
import { queryClient } from '../../api/queryClient';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installUsersBackend, manyClients, newUsersState, type UsersState } from '../../test/fakeUsersApi';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), delete: vi.fn() } }));
// The companies have a page of their own; here it is only a stand-in that says it is shown.
vi.mock('./CompaniesPage', () => ({ default: () => <p>the companies section</p> }));

const asAdmin = () => useAuthStore.setState({ accessToken: 't', user: { id: 'a1', role: 'Admin', firstName: 'Admin', lastName: 'Tester', departmentId: null } as never });
const asEmployee = () => useAuthStore.setState({ accessToken: 't', user: { id: 'e1', role: 'Employee', firstName: 'Amra', lastName: 'Ahmetović', departmentId: 'd1' } as never });

let server: ReturnType<typeof installUsersBackend>;
const originalDefaults = queryClient.getDefaultOptions();

function renderPage(address = '/users', state: UsersState = newUsersState(), before?: (server: ReturnType<typeof installUsersBackend>) => void) {
  server = installUsersBackend(state);
  before?.(server);
  return renderWithQuery(
    <MemoryRouter initialEntries={[address]}>
      <UsersPage />
    </MemoryRouter>,
    queryClient
  );
}

const newUser = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
const settle = () => act(async () => { await vi.advanceTimersByTimeAsync(400); });
const pageRequests = () => server.sent('GET', /^\/api\/users\/paged$/);
const lastPaged = () => pageRequests().at(-1)!.params as Record<string, unknown>;
const bodyRows = () => within(screen.getAllByRole('rowgroup')[1]).getAllByRole('row');
const cellsOf = (row: HTMLElement) => within(row).getAllByRole('cell').map((c) => c.textContent);
const names = () => bodyRows().map((r) => cellsOf(r)[0]);
const clientsTab = () => screen.getByText(/^Clients \(/);
const staffTab = () => screen.getByText(/^Employees & Admins \(/);
const section = (name: string) => screen.getAllByText(name).find((el) => el.tagName === 'BUTTON')!;
const search = () => screen.getByPlaceholderText(/^Search by name or company/);

describe('the users page', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    queryClient.clear();
    queryClient.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false } });
    asAdmin();
  });
  afterEach(() => vi.useRealTimers());
  afterAll(() => queryClient.setDefaultOptions(originalDefaults));

  describe('the two sections', () => {
    it('opens on the accounts, with the form to make one, and shows the companies when they are chosen, and the accounts again', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      expect(screen.getByRole('heading', { name: 'Create account' })).toBeInTheDocument();
      expect(screen.queryByText('the companies section')).not.toBeInTheDocument();

      await user.click(section('Companies'));
      expect(screen.getByText('the companies section')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Create account' })).not.toBeInTheDocument();

      await user.click(section('Users'));
      expect(await screen.findByText('Mia Marić')).toBeInTheDocument();
    });

    it('opens on the companies when the address says so, and asks for no accounts while they are shown', async () => {
      renderPage('/users?tab=companies');

      expect(screen.getByText('the companies section')).toBeInTheDocument();
      expect(pageRequests()).toHaveLength(0);
    });
  });

  describe('what each kind of person sees', () => {
    it('shows an administrator the clients and the staff, with how many of each, and all three kinds of account to make', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(clientsTab()).toHaveTextContent('Clients (3)');
      expect(staffTab()).toHaveTextContent('Employees & Admins (2)');
      expect(within(screen.getByLabelText('Account type')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Client', 'Employee', 'Admin']);
      expect(screen.queryByText(/You see the clients who have tickets in your branch/)).not.toBeInTheDocument();
    });

    it('shows the staff only the clients, a note on why, and only clients to make', async () => {
      asEmployee();
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.queryByText(/^Clients \(/)).not.toBeInTheDocument();
      expect(screen.queryByText(/^Employees & Admins \(/)).not.toBeInTheDocument();
      expect(screen.getByText(/You see the clients who have tickets in your branch/)).toBeInTheDocument();
      expect(within(screen.getByLabelText('Account type')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Client']);
      expect(lastPaged().group).toBe('clients');
    });
  });

  describe('the list', () => {
    it('asks for the first page of clients, 25 to a page, with no search, and shows each account', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(lastPaged()).toEqual({ group: 'clients', search: undefined, page: 1, pageSize: 25 });
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Name', 'Email', 'Company', 'Status']);
      expect(cellsOf(bodyRows()[0])).toEqual(['Mia Marić', 'mia.marić@firma.test', 'Acme', 'Active']);
      expect(cellsOf(bodyRows()[2])).toEqual(['Olja Ostojić', 'olja.ostojić@firma.test', 'Acme', 'Inactive']);
      expect(screen.getByRole('link', { name: 'Mia Marić' })).toHaveAttribute('href', '/users/k1');
    });

    it('shows the staff with their branch, or a dash when they have none, when that tab is chosen', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');

      await user.click(staffTab());

      expect(await screen.findByText('Amra Ahmetović')).toBeInTheDocument();
      expect(lastPaged().group).toBe('staff');
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Name', 'Email', 'Branch', 'Status']);
      expect(names()).toEqual(['Amra Ahmetović', 'Admin Tester']);
      expect(cellsOf(bodyRows()[0])[2]).toBe('Sarajevo');
      expect(cellsOf(bodyRows()[1])[2]).toBe('—');
    });

    it('says how many accounts there are, in the singular for one, and that they match when something is searched for', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      expect(screen.getByText('3 accounts')).toBeInTheDocument();

      await user.type(search(), 'Mia');
      await settle();

      expect(await screen.findByText('1 account match')).toBeInTheDocument();
    });

    it('asks the server once for what was typed, without the spaces around it, and not once per letter', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');

      await user.type(search(), '  Beta ');
      await settle();

      await waitFor(() => expect(names()).toEqual(['Nedim Nuhić']));
      expect(pageRequests().map((r) => (r.params as { search?: string }).search)).toEqual([undefined, 'Beta']);
    });

    it('asks for no search when what was typed is only spaces', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');

      await user.type(search(), '   ');
      await settle();

      expect(pageRequests()).toHaveLength(1);
    });

    it('says that nothing matches the search, and that there are no accounts when none are searched for', async () => {
      const user = newUser();
      renderPage('/users', { ...newUsersState(), accounts: [] });
      expect(await screen.findByText('No accounts here yet.')).toBeInTheDocument();

      await user.type(search(), 'zzz');
      await settle();

      expect(await screen.findByText('No accounts match your search.')).toBeInTheDocument();
    });

    it('has pages of 25, goes forward and back, and cannot go past either end', async () => {
      const user = newUser();
      renderPage('/users', { ...newUsersState(), accounts: manyClients(60) });
      await screen.findByText('Page 1 of 3');
      expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
      expect(bodyRows()).toHaveLength(25);

      await user.click(screen.getByRole('button', { name: 'Next' }));
      expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument();
      expect(lastPaged().page).toBe(2);
      expect(names()[0]).toBe('Klijent026 Test');

      await user.click(screen.getByRole('button', { name: 'Next' }));
      expect(await screen.findByText('Page 3 of 3')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
      expect(bodyRows()).toHaveLength(10);

      await user.click(screen.getByRole('button', { name: 'Previous' }));
      expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument();
    });

    it('has no pages to go between when everything fits on one', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();
    });

    it('goes back to the first page when something is searched for, and when the other tab is chosen', async () => {
      const user = newUser();
      const state = { ...newUsersState(), accounts: [...manyClients(60), ...newUsersState().accounts.filter((a) => a.role !== 'Client')] };
      renderPage('/users', state);
      await user.click(await screen.findByRole('button', { name: 'Next' }));
      await screen.findByText('Page 2 of 3');

      await user.type(search(), 'Klijent05');
      await settle();
      await waitFor(() => expect(lastPaged()).toMatchObject({ search: 'Klijent05', page: 1 }));
      await user.clear(search());
      await settle();
      await user.click(await screen.findByRole('button', { name: 'Next' }));
      await screen.findByText('Page 2 of 3');

      await user.click(staffTab());
      await screen.findByText('Amra Ahmetović');
      expect(lastPaged()).toMatchObject({ group: 'staff', page: 1 });
    });

    it('says what the server said when the accounts could not be loaded, or plainly when it said nothing', async () => {
      const first = renderPage('/users', newUsersState(), (s) => s.refuse('GET', /users\/paged$/, 500, { message: 'The database is busy.' }));
      expect(await screen.findByText('The database is busy.')).toBeInTheDocument();
      first.unmount();

      renderPage('/users', newUsersState(), (s) => s.refuse('GET', /users\/paged$/, 500, {}));
      expect(await screen.findByText('Could not load the accounts.')).toBeInTheDocument();
    });

    it('shows the answer to the latest search even when an earlier one arrives last', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      const early = server.hold('GET', /users\/paged$/);

      await user.type(search(), 'Mia');
      await settle();
      await user.clear(search());
      await user.type(search(), 'Beta');
      await settle();
      await waitFor(() => expect(names()).toEqual(['Nedim Nuhić']));
      early.release();
      await act(async () => { await vi.advanceTimersByTimeAsync(50); });

      expect(names()).toEqual(['Nedim Nuhić']);
    });
  });

  describe('making an account', () => {
    async function fill(user: ReturnType<typeof newUser>, o: { first?: string; last?: string; email?: string } = {}) {
      await user.type(screen.getByLabelText('First name'), o.first ?? 'Pero');
      await user.type(screen.getByLabelText('Last name'), o.last ?? 'Perić');
      await user.type(screen.getByLabelText('Email'), o.email ?? 'pero.peric@firma.test');
    }

    it('makes a client of a company, shows the temporary password, empties the form, and the client is in the list', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await screen.findByRole('option', { name: 'Beta d.o.o.' });
      await fill(user);
      await user.selectOptions(screen.getByLabelText('Company'), 'c2');

      await user.click(screen.getByRole('button', { name: 'Create account' }));

      const dialog = await screen.findByRole('dialog', { name: 'Account created' });
      expect(within(dialog).getByText('pero.peric@firma.test')).toBeInTheDocument();
      expect(within(dialog).getByText(/^Tmp#\d+$/)).toBeInTheDocument();
      expect(server.sent('POST', /users\/clients$/).map((r) => r.body)).toEqual([{ firstName: 'Pero', lastName: 'Perić', email: 'pero.peric@firma.test', companyId: 'c2' }]);
      expect(screen.getByLabelText('First name')).toHaveValue('');
      expect(screen.getByLabelText('Last name')).toHaveValue('');
      expect(screen.getByLabelText('Email')).toHaveValue('');
      expect(screen.getByLabelText('Company')).toHaveValue('');
      expect(await screen.findByText('Pero Perić')).toBeInTheDocument();
      expect(clientsTab()).toHaveTextContent('Clients (4)');

      await user.click(within(dialog).getByRole('button', { name: 'Done' }));
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('wants a company for a client', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await fill(user);

      await user.click(screen.getByRole('button', { name: 'Create account' }));

      expect(screen.getByText('Please select a company.')).toBeInTheDocument();
      expect(server.sent('POST', /users\/clients$/)).toHaveLength(0);
    });

    it('makes an employee of a branch, with the sub-branch when the branch has some, and offers the branches with their addresses', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await fill(user, { first: 'Sara', last: 'Sarić', email: 'sara.saric@firma.test' });
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      expect(screen.queryByLabelText('Company')).not.toBeInTheDocument();
      await screen.findByRole('option', { name: 'Mostar (mostar@firma.test)' });

      await user.click(screen.getByRole('button', { name: 'Create account' }));
      expect(screen.getByText('Please select a branch.')).toBeInTheDocument();

      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');
      await screen.findByLabelText('Sub-branch');
      await user.click(screen.getByRole('button', { name: 'Create account' }));
      expect(screen.getByText('Please select a sub-branch.')).toBeInTheDocument();

      await user.selectOptions(screen.getByLabelText('Sub-branch'), 's1');
      await user.click(screen.getByRole('button', { name: 'Create account' }));

      await screen.findByRole('dialog', { name: 'Account created' });
      expect(server.sent('POST', /users\/employees$/).map((r) => r.body)).toEqual([
        { firstName: 'Sara', lastName: 'Sarić', email: 'sara.saric@firma.test', role: 'Employee', departmentId: 'd2', subBranchId: 's1' },
      ]);
    });

    it('does not ask for a sub-branch of a branch that has none', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await fill(user);
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      await screen.findByRole('option', { name: 'Sarajevo (sarajevo@firma.test)' });

      await user.selectOptions(screen.getByLabelText('Branch'), 'd1');
      await waitFor(() => expect(server.sent('GET', /d1\/sub-branches$/)).toHaveLength(1));
      await user.click(screen.getByRole('button', { name: 'Create account' }));

      await screen.findByRole('dialog', { name: 'Account created' });
      expect(server.sent('POST', /users\/employees$/)[0].body).toMatchObject({ departmentId: 'd1', subBranchId: null });
    });

    it('makes an administrator with no branch', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await fill(user, { email: 'admin2@firma.test' });
      await user.selectOptions(screen.getByLabelText('Account type'), 'Admin');
      expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Company')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Create account' }));

      await screen.findByRole('dialog', { name: 'Account created' });
      expect(server.sent('POST', /users\/employees$/)[0].body).toMatchObject({ role: 'Admin', departmentId: null, subBranchId: null });
    });

    it('starts with the company that the address names', async () => {
      renderPage('/users?companyId=c2');
      await screen.findByText('Mia Marić');
      await screen.findByRole('option', { name: 'Beta d.o.o.' });

      expect(screen.getByLabelText('Company')).toHaveValue('c2');
    });

    it('limits the names and the address to what the server accepts', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getByLabelText('First name')).toHaveAttribute('maxlength', '25');
      expect(screen.getByLabelText('Last name')).toHaveAttribute('maxlength', '30');
      expect(screen.getByLabelText('Email')).toHaveAttribute('maxlength', '100');
    });

    it('says what the server said when it refuses, or plainly when it said nothing, and keeps what was written', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await screen.findByRole('option', { name: 'Acme' });
      await fill(user, { email: 'admin.tester@firma.test' });
      await user.selectOptions(screen.getByLabelText('Company'), 'c1');

      await user.click(screen.getByRole('button', { name: 'Create account' }));
      expect(await screen.findByText('An account with that email already exists.')).toBeInTheDocument();

      server.refuse('POST', /users\/clients$/, 500, {});
      await user.click(screen.getByRole('button', { name: 'Create account' }));
      expect(await screen.findByText('Could not create the account.')).toBeInTheDocument();
      expect(screen.getByLabelText('First name')).toHaveValue('Pero');
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('cannot be sent twice while it is being made', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await screen.findByRole('option', { name: 'Acme' });
      await fill(user);
      await user.selectOptions(screen.getByLabelText('Company'), 'c1');
      const hold = server.hold('POST', /users\/clients$/);

      await user.click(screen.getByRole('button', { name: 'Create account' }));

      expect(screen.getByRole('button', { name: 'Creating…' })).toBeDisabled();
      hold.release();
      await screen.findByRole('dialog', { name: 'Account created' });
      expect(server.sent('POST', /users\/clients$/)).toHaveLength(1);
    });
  });

  describe('what it used to get wrong', () => {
    it('says that the accounts are on their way, and not that there are none, until they have come', async () => {
      const holding = { release: () => {} };
      renderPage('/users', newUsersState(), (s) => Object.assign(holding, s.hold('GET', /users\/paged$/)));

      expect(await screen.findByText('Loading accounts…')).toBeInTheDocument();
      expect(screen.queryByText('No accounts here yet.')).not.toBeInTheDocument();

      holding.release();
      expect(await screen.findByText('Mia Marić')).toBeInTheDocument();
      expect(screen.queryByText('Loading accounts…')).not.toBeInTheDocument();
    });

    it('does not show the clients under the staff tab while the staff are on their way, and keeps the totals in the tabs', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      const staff = server.hold('GET', /users\/paged$/);

      await user.click(staffTab());

      expect(screen.queryByText('Mia Marić')).not.toBeInTheDocument();
      expect(screen.getByText('Loading accounts…')).toBeInTheDocument();
      expect(clientsTab()).toHaveTextContent('Clients (3)');
      expect(staffTab()).toHaveTextContent('Employees & Admins (2)');
      staff.release();
      expect(await screen.findByText('Amra Ahmetović')).toBeInTheDocument();
    });

    it('shows why, and none of the clients, when the staff cannot be loaded', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      server.refuse('GET', /users\/paged$/, 500, { message: 'The database is busy.' });

      await user.click(staffTab());

      expect(await screen.findByText('The database is busy.')).toBeInTheDocument();
      expect(screen.queryByText('Mia Marić')).not.toBeInTheDocument();
      expect(screen.queryByText('No accounts here yet.')).not.toBeInTheDocument();
      expect(screen.queryByText('Loading accounts…')).not.toBeInTheDocument();
    });

    it('does not show the pages while the next page is on its way, since it does not know yet how many there are', async () => {
      const user = newUser();
      renderPage('/users', { ...newUsersState(), accounts: manyClients(60) });
      await user.click(await screen.findByRole('button', { name: 'Next' }));
      await screen.findByText('Page 2 of 3');
      const third = server.hold('GET', /users\/paged$/);

      await user.click(screen.getByRole('button', { name: 'Next' }));

      expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument();
      third.release();
      expect(await screen.findByText('Page 3 of 3')).toBeInTheDocument();
    });

    it('says that there are no accounts across the whole width of the table', async () => {
      renderPage('/users', { ...newUsersState(), accounts: [] });

      expect((await screen.findByText('No accounts here yet.')).closest('td')).toHaveAttribute('colspan', '4');
    });

    it('does not ask for the branches, or their sub-branches, until an employee is to be made', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await screen.findByRole('option', { name: 'Acme' });
      expect(server.sent('GET', /\/api\/departments$/)).toHaveLength(0);
      expect(server.sent('GET', /sub-branches$/)).toHaveLength(0);

      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');

      await screen.findByRole('option', { name: 'Mostar (mostar@firma.test)' });
      expect(server.sent('GET', /\/api\/departments$/)).toHaveLength(1);
    });

    it('lets go of the sub-branch that was chosen when another branch is chosen, so that it is not sent with a branch it is not in', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await user.type(screen.getByLabelText('First name'), 'Sara');
      await user.type(screen.getByLabelText('Last name'), 'Sarić');
      await user.type(screen.getByLabelText('Email'), 'sara.saric@firma.test');
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      await screen.findByRole('option', { name: 'Mostar (mostar@firma.test)' });
      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');
      await user.selectOptions(await screen.findByLabelText('Sub-branch'), 's1');

      await user.selectOptions(screen.getByLabelText('Branch'), 'd1');
      await waitFor(() => expect(screen.queryByLabelText('Sub-branch')).not.toBeInTheDocument());
      await user.click(screen.getByRole('button', { name: 'Create account' }));

      await screen.findByRole('dialog', { name: 'Account created' });
      expect(server.sent('POST', /users\/employees$/)[0].body).toMatchObject({ departmentId: 'd1', subBranchId: null });
    });

    it('does not send the branch or the sub-branch that was chosen for an employee when the account is made an administrator\'s after all', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await user.type(screen.getByLabelText('First name'), 'Sara');
      await user.type(screen.getByLabelText('Last name'), 'Sarić');
      await user.type(screen.getByLabelText('Email'), 'sara.saric@firma.test');
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      await screen.findByRole('option', { name: 'Mostar (mostar@firma.test)' });
      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');
      await user.selectOptions(await screen.findByLabelText('Sub-branch'), 's1');

      await user.selectOptions(screen.getByLabelText('Account type'), 'Admin');
      await user.click(screen.getByRole('button', { name: 'Create account' }));

      await screen.findByRole('dialog', { name: 'Account created' });
      expect(server.sent('POST', /users\/employees$/)[0].body).toMatchObject({ role: 'Admin', departmentId: null, subBranchId: null });
    });

    it('empties the form after an employee is made, the branch and the sub-branch too, and keeps the kind of account', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await user.type(screen.getByLabelText('First name'), 'Sara');
      await user.type(screen.getByLabelText('Last name'), 'Sarić');
      await user.type(screen.getByLabelText('Email'), 'sara.saric@firma.test');
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      await screen.findByRole('option', { name: 'Mostar (mostar@firma.test)' });
      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');
      await user.selectOptions(await screen.findByLabelText('Sub-branch'), 's1');

      await user.click(screen.getByRole('button', { name: 'Create account' }));

      await screen.findByRole('dialog', { name: 'Account created' });
      expect(screen.getByLabelText('First name')).toHaveValue('');
      expect(screen.getByLabelText('Branch')).toHaveValue('');
      expect(screen.queryByLabelText('Sub-branch')).not.toBeInTheDocument();
      expect(screen.getByLabelText('Account type')).toHaveValue('Employee');
    });

    it('asks only for the first page when the other tab is chosen from a later one, and not for a page that the other tab may not have', async () => {
      const user = newUser();
      renderPage('/users', { ...newUsersState(), accounts: [...manyClients(60), ...newUsersState().accounts.filter((a) => a.role !== 'Client')] });
      await user.click(await screen.findByRole('button', { name: 'Next' }));
      await screen.findByText('Page 2 of 3');

      await user.click(staffTab());
      await screen.findByText('Amra Ahmetović');

      expect(pageRequests().filter((r) => (r.params as { group: string }).group === 'staff').map((r) => (r.params as { page: number }).page)).toEqual([1]);
    });

    it('asks only for the first page of what is searched for, when it is searched for from a later one', async () => {
      const user = newUser();
      renderPage('/users', { ...newUsersState(), accounts: manyClients(60) });
      await user.click(await screen.findByRole('button', { name: 'Next' }));
      await screen.findByText('Page 2 of 3');

      await user.type(search(), 'Klijent05');
      await settle();
      await waitFor(() => expect(lastPaged()).toMatchObject({ search: 'Klijent05' }));

      expect(pageRequests().filter((r) => (r.params as { search?: string }).search === 'Klijent05').map((r) => (r.params as { page: number }).page)).toEqual([1]);
    });

    it('does not take a request that was cancelled for a failure', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      const early = server.hold('GET', /users\/paged$/);

      await user.type(search(), 'Mia');
      await settle();
      await user.clear(search());
      await user.type(search(), 'Beta');
      await settle();
      await waitFor(() => expect(names()).toEqual(['Nedim Nuhić']));
      early.release();
      await act(async () => { await vi.advanceTimersByTimeAsync(50); });

      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('sends the names without the spaces around them', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await screen.findByRole('option', { name: 'Acme' });
      await user.type(screen.getByLabelText('First name'), '  Pero ');
      await user.type(screen.getByLabelText('Last name'), ' Perić  ');
      await user.type(screen.getByLabelText('Email'), 'pero.peric@firma.test');
      await user.selectOptions(screen.getByLabelText('Company'), 'c1');

      await user.click(screen.getByRole('button', { name: 'Create account' }));

      await screen.findByRole('dialog', { name: 'Account created' });
      expect(server.sent('POST', /users\/clients$/)[0].body).toMatchObject({ firstName: 'Pero', lastName: 'Perić' });
    });

    it.each([
      ['a first name of nothing but spaces', { first: '   ', last: 'Perić' }],
      ['a last name of nothing but spaces', { first: 'Pero', last: '   ' }],
    ])('does not make an account with %s, which the form lets through because something was written', async (_name, written) => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await screen.findByRole('option', { name: 'Acme' });
      await user.type(screen.getByLabelText('First name'), written.first);
      await user.type(screen.getByLabelText('Last name'), written.last);
      await user.type(screen.getByLabelText('Email'), 'pero.peric@firma.test');
      await user.selectOptions(screen.getByLabelText('Company'), 'c1');

      await user.click(screen.getByRole('button', { name: 'Create account' }));

      expect(screen.getByText('First name and last name are required.')).toBeInTheDocument();
      expect(server.sent('POST', /users\/clients$/)).toHaveLength(0);
    });

    it('says when the companies could not be loaded, since a client could then not be given one', async () => {
      renderPage('/users', newUsersState(), (s) => s.refuse('GET', /\/api\/companies$/, 500, {}, -1));

      expect(await screen.findByText('The list of companies could not be loaded.')).toBeInTheDocument();
    });

    it('says when the branches could not be loaded, once an employee is to be made', async () => {
      const user = newUser();
      renderPage('/users', newUsersState(), (s) => s.refuse('GET', /\/api\/departments$/, 500, {}, -1));
      await screen.findByText('Mia Marić');
      expect(screen.queryByText('The list of branches could not be loaded.')).not.toBeInTheDocument();

      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');

      expect(await screen.findByText('The list of branches could not be loaded.')).toBeInTheDocument();
    });

    it('says when the sub-branches of the chosen branch could not be loaded, so that it is not taken for a branch that has none', async () => {
      const user = newUser();
      renderPage('/users', newUsersState(), (s) => s.refuse('GET', /d2\/sub-branches$/, 500, {}, -1));
      await screen.findByText('Mia Marić');
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      await screen.findByRole('option', { name: 'Mostar (mostar@firma.test)' });

      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');

      expect(await screen.findByText('The list of sub-branches could not be loaded.')).toBeInTheDocument();
    });

    it('does not say any of that when the lists could be loaded', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      await screen.findByRole('option', { name: 'Mostar (mostar@firma.test)' });
      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');
      await screen.findByLabelText('Sub-branch');

      expect(screen.queryByText(/could not be loaded/)).not.toBeInTheDocument();
    });

    it('names the search box, so that a screen reader can tell what it is for', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getByRole('textbox', { name: 'Search accounts' })).toBeInTheDocument();
    });

    it('has the two sections as tabs, with the one that is chosen said to be chosen', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getAllByRole('tab').filter((x) => ['Users', 'Companies'].includes(x.textContent ?? '')).map((x) => [x.textContent, x.getAttribute('aria-selected')])).toEqual([
        ['Users', 'true'],
        ['Companies', 'false'],
      ]);

      await user.click(screen.getByRole('tab', { name: 'Companies' }));
      expect(screen.getByRole('tab', { name: 'Companies' })).toHaveAttribute('aria-selected', 'true');
      expect(screen.getByRole('tab', { name: 'Users' })).toHaveAttribute('aria-selected', 'false');
    });

    it('has the clients and the staff as tabs, with the one that is chosen said to be chosen', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getByRole('tab', { name: /^Clients/ })).toHaveAttribute('aria-selected', 'true');
      expect(screen.getByRole('tab', { name: /^Employees/ })).toHaveAttribute('aria-selected', 'false');

      await user.click(screen.getByRole('tab', { name: /^Employees/ }));
      expect(screen.getByRole('tab', { name: /^Employees/ })).toHaveAttribute('aria-selected', 'true');
      expect(screen.getByRole('tab', { name: /^Clients/ })).toHaveAttribute('aria-selected', 'false');
    });
  });
});
