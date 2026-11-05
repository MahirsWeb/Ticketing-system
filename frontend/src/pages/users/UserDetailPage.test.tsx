import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import { QueryClient } from '@tanstack/react-query';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { format } from 'date-fns';
import UserDetailPage from './UserDetailPage';
import { usersApi } from '../../api/users';
import { ticketsApi } from '../../api/tickets';
import { lookupsApi } from '../../api/lookups';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { TicketListItem, UserListItemDto } from '../../types';

vi.mock('../../api/users', () => ({
  usersApi: { getById: vi.fn(), updateUser: vi.fn(), regenerateTempPassword: vi.fn(), setActive: vi.fn(), exportData: vi.fn(), anonymize: vi.fn(), uploadProfilePicture: vi.fn() },
}));
vi.mock('../../api/tickets', () => ({ ticketsApi: { list: vi.fn() } }));
vi.mock('../../api/lookups', () => ({ lookupsApi: { companies: vi.fn(), departments: vi.fn(), subBranches: vi.fn() } }));

const client: UserListItemDto = {
  id: 'u1', firstName: 'Kemal', lastName: 'Klijent', email: 'kemal@firma.test', role: 'Client', companyId: 'co1', companyName: 'Acme', phoneNumber: '+38761000001',
  isActive: true, emailConfirmed: true, createdAtUtc: '2026-01-05T10:00:00Z', departmentId: null, departmentName: null, subBranchId: null, subBranchName: null,
  profilePictureUrl: null, lastLoginAtUtc: null,
};
const employee: UserListItemDto = {
  ...client, id: 'e1', firstName: 'Amra', lastName: 'Agent', email: 'amra@firma.test', role: 'Employee', companyId: null, companyName: null, phoneNumber: null,
  departmentId: 'd1', departmentName: 'Sarajevo', subBranchId: 's1', subBranchName: 'Centar', lastLoginAtUtc: '2026-03-02T08:30:00Z',
};
const otherClient: UserListItemDto = { ...client, id: 'u2', firstName: 'Lejla', lastName: 'Kupac', email: 'lejla@firma.test' };
const admin = { id: 'admin1', role: 'Admin', firstName: 'Admin', lastName: 'Tester' } as never;
const staffUser = { id: 'staff1', role: 'Employee', firstName: 'Dino', lastName: 'Dinić', departmentId: 'd1' };
const staff = staffUser as never;

const ticket = (id: string, number: string, title: string): TicketListItem =>
  ({ id, ticketNumber: number, title, status: 'Open', priority: 'Medium', clientName: 'Kemal Klijent', assignedToName: 'Amra Agent', createdAt: '2026-02-01T09:00:00Z' }) as unknown as TicketListItem;
const page = (items: TicketListItem[]) => ({ items, totalCount: items.length, page: 1, pageSize: 100 });

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function GoTo({ to }: { to: string }) {
  const navigate = useNavigate();
  return <button onClick={() => navigate(to)}>{`go to ${to}`}</button>;
}

const renderPage = (path = '/users/u1') =>
  renderWithQuery(
    <MemoryRouter initialEntries={[path]}>
      <GoTo to="/users/e1" />
      <GoTo to="/users/u1" />
      <GoTo to="/users/u2" />
      <Routes>
        <Route path="/users/:id" element={<UserDetailPage />} />
        <Route path="/users" element={<p>the users</p>} />
        <Route path="/tickets/:id" element={<p>the ticket page</p>} />
      </Routes>
    </MemoryRouter>
  );

/** The value shown under a label of the profile. */
const field = (label: string) => screen.getByText(label, { selector: 'div' }).nextElementSibling as HTMLElement;
const dialog = () => screen.getByRole('dialog');

describe('the page of one user', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 't', user: admin });
    vi.mocked(usersApi.getById).mockReset().mockImplementation(async (id) => (id === 'e1' ? employee : id === 'u2' ? otherClient : client));
    for (const fn of [usersApi.updateUser, usersApi.regenerateTempPassword, usersApi.setActive, usersApi.exportData, usersApi.anonymize, usersApi.uploadProfilePicture]) vi.mocked(fn).mockReset();
    vi.mocked(usersApi.updateUser).mockResolvedValue(undefined as never);
    vi.mocked(usersApi.setActive).mockResolvedValue(undefined as never);
    vi.mocked(usersApi.anonymize).mockResolvedValue(undefined as never);
    vi.mocked(usersApi.uploadProfilePicture).mockResolvedValue({ profilePictureUrl: '/pictures/x.png' });
    vi.mocked(ticketsApi.list).mockReset().mockResolvedValue(page([ticket('t1', '100001', 'Printer ne radi'), ticket('t2', '100002', 'Skener')]) as never);
    vi.mocked(lookupsApi.companies).mockReset().mockResolvedValue([{ id: 'co1', name: 'Acme' }, { id: 'co2', name: 'Globex' }] as never);
    vi.mocked(lookupsApi.departments).mockReset().mockResolvedValue([{ id: 'd1', name: 'Sarajevo', email: 's@x.test' }, { id: 'd2', name: 'Mostar', email: 'm@x.test' }] as never);
    vi.mocked(lookupsApi.subBranches).mockReset().mockImplementation((async (departmentId: string) => (departmentId === 'd1' ? [{ id: 's1', name: 'Centar' }, { id: 's2', name: 'Ilidža' }] : [])) as never);
  });
  afterEach(() => vi.restoreAllMocks());

  describe('what it shows', () => {
    it('shows a client with the e-mail, the phone, the organization, the status and when they last signed in', async () => {
      renderPage();

      expect(await screen.findByRole('heading', { name: 'Kemal Klijent' })).toBeInTheDocument();
      expect(screen.getByText('Client', { selector: 'span' })).toBeInTheDocument();
      expect(field('Email')).toHaveTextContent('kemal@firma.test');
      expect(field('Phone')).toHaveTextContent('+38761000001');
      expect(field('Organization')).toHaveTextContent('Acme');
      expect(field('Status')).toHaveTextContent('Active');
      expect(field('Email verified')).toHaveTextContent('Yes');
      expect(field('Created')).toHaveTextContent(format(new Date(client.createdAtUtc), 'dd.MM.yyyy'));
      expect(field('Last login')).toHaveTextContent('Never');
    });

    it('shows a member of the staff with the branch and the sub-branch, and no phone', async () => {
      renderPage('/users/e1');

      expect(await screen.findByRole('heading', { name: 'Amra Agent' })).toBeInTheDocument();
      expect(screen.queryByText('Phone', { selector: 'div' })).not.toBeInTheDocument();
      expect(field('Branch')).toHaveTextContent('Sarajevo / Centar');
      expect(field('Last login')).toHaveTextContent(format(new Date(employee.lastLoginAtUtc!), 'dd.MM.yyyy HH:mm'));
    });

    it('shows a dash when there is no organization or branch, and "No" for an e-mail that was not verified, and "Inactive" for a switched-off account', async () => {
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, companyName: null, emailConfirmed: false, isActive: false });

      renderPage();

      await screen.findByRole('heading', { name: 'Kemal Klijent' });
      expect(field('Organization')).toHaveTextContent('—');
      expect(field('Email verified')).toHaveTextContent('No');
      expect(field('Status')).toHaveTextContent('Inactive');
    });

    it('says the user was not found when the server does not know them', async () => {
      vi.mocked(usersApi.getById).mockRejectedValue({ response: { status: 404 } });

      renderPage();

      expect(await screen.findByText('User not found.')).toBeInTheDocument();
    });

    it('does not show a phone for a member of the staff, even when one is on record', async () => {
      vi.mocked(usersApi.getById).mockResolvedValue({ ...employee, phoneNumber: '+38761999999' });

      renderPage('/users/e1');

      await screen.findByRole('heading', { name: 'Amra Agent' });
      expect(screen.queryByText('+38761999999')).not.toBeInTheDocument();
    });

    it('shows a spinner while the user is on its way', async () => {
      vi.mocked(usersApi.getById).mockReturnValue(new Promise(() => {}));

      renderPage();

      expect(document.querySelector('svg.animate-spin')).toBeInTheDocument();
      expect(screen.queryByText('User not found.')).not.toBeInTheDocument();
    });

    it('has a way back to all users', async () => {
      const user = userEvent.setup();
      renderPage();

      await user.click(await screen.findByRole('link', { name: '← All users' }));

      expect(screen.getByText('the users')).toBeInTheDocument();
    });
  });

  describe('the tickets', () => {
    it('asks for the tickets a client submitted, and lists them with who has them', async () => {
      renderPage();

      expect(await screen.findByText('Printer ne radi')).toBeInTheDocument();
      expect(vi.mocked(ticketsApi.list).mock.calls).toEqual([[{ clientId: 'u1', pageSize: 100 }]]);
      expect(screen.getByText('Tickets submitted (2)')).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Assigned to' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: '#100001' })).toBeInTheDocument();
      expect(screen.getAllByText('Amra Agent').length).toBeGreaterThan(0);
    });

    it('asks for the tickets a member of the staff has, and lists them with their clients', async () => {
      renderPage('/users/e1');

      expect(await screen.findByText('Printer ne radi')).toBeInTheDocument();
      expect(vi.mocked(ticketsApi.list).mock.calls).toEqual([[{ assignedToUserId: 'e1', pageSize: 100 }]]);
      expect(screen.getByText('Tickets assigned (2)')).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Client' })).toBeInTheDocument();
    });

    it('says when there are none', async () => {
      vi.mocked(ticketsApi.list).mockResolvedValue(page([]) as never);

      renderPage();

      expect(await screen.findByText('No tickets found.')).toBeInTheDocument();
      expect(screen.getByText('Tickets submitted (0)')).toBeInTheDocument();
    });

    it('opens a ticket by its row or by its number', async () => {
      const user = userEvent.setup();
      const first = renderPage();
      await user.click(await screen.findByText('Printer ne radi'));
      expect(screen.getByText('the ticket page')).toBeInTheDocument();
      first.unmount();

      renderPage();
      await user.click(await screen.findByRole('link', { name: '#100002' }));
      expect(screen.getByText('the ticket page')).toBeInTheDocument();
    });
  });

  describe('who may do what', () => {
    it('gives an administrator the actions, and not the anonymizing of themselves', async () => {
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, id: 'admin1' });
      renderPage('/users/admin1');

      expect(await screen.findByRole('button', { name: /Edit account details/ })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Deactivate account' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Regenerate temp password' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Export personal data' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Anonymize…' })).not.toBeInTheDocument();
    });

    it('does not offer anonymizing an account that has been anonymized already', async () => {
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, email: 'x@anonymized.invalid' });
      renderPage();

      await screen.findByRole('button', { name: 'Export personal data' });

      expect(screen.queryByRole('button', { name: 'Anonymize…' })).not.toBeInTheDocument();
    });

    it('gives a member of the staff none of the actions of an administrator and no way to change the picture of somebody else', async () => {
      useAuthStore.setState({ accessToken: 't', user: staff });
      renderPage();

      await screen.findByRole('heading', { name: 'Kemal Klijent' });

      for (const name of [/Edit account details/, 'Deactivate account', 'Regenerate temp password', 'Export personal data', 'Anonymize…']) {
        expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
      }
      expect(screen.queryByRole('button', { name: 'Change' })).not.toBeInTheDocument();
    });

    it('lets a person change their own picture, whatever their role', async () => {
      useAuthStore.setState({ accessToken: 't', user: { ...staffUser, id: 'u1' } as never });
      renderPage();

      expect(await screen.findByRole('button', { name: 'Change' })).toBeInTheDocument();
    });
  });

  describe('editing the account', () => {
    const open = async (user: ReturnType<typeof userEvent.setup>) => {
      await user.click(await screen.findByRole('button', { name: /Edit account details/ }));
    };

    it('opens with the values the account has, and closes again', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);

      expect(screen.getByLabelText('First name')).toHaveValue('Kemal');
      expect(screen.getByLabelText('Last name')).toHaveValue('Klijent');
      expect(screen.getByLabelText('Account type')).toHaveValue('Client');
      await waitFor(() => expect(screen.getByLabelText('Company')).toHaveValue('co1'));
      expect(screen.getByRole('button', { name: /Close/ })).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /Close/ }));
      expect(screen.getByRole('button', { name: /Edit account details/ })).toBeInTheDocument();
    });

    it('saves a client with the company, and shows the new name', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Company')).toHaveValue('co1'));
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, firstName: 'Kemo', companyId: 'co2', companyName: 'Globex' });

      await user.clear(screen.getByLabelText('First name'));
      await user.type(screen.getByLabelText('First name'), 'Kemo');
      await user.selectOptions(screen.getByLabelText('Company'), 'co2');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByRole('heading', { name: 'Kemo Klijent' })).toBeInTheDocument();
      expect(vi.mocked(usersApi.updateUser).mock.calls).toEqual([['u1', { firstName: 'Kemo', lastName: 'Klijent', role: 'Client', companyId: 'co2', departmentId: undefined, subBranchId: undefined }]]);
      expect(field('Organization')).toHaveTextContent('Globex');
      expect(screen.getByRole('button', { name: /Edit account details/ })).toBeInTheDocument(); // the form is closed
    });

    it('asks for a company before it saves a client without one', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Company')).toHaveValue('co1'));

      await user.selectOptions(screen.getByLabelText('Company'), '');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(screen.getByText('Please select a company.')).toBeInTheDocument();
      expect(usersApi.updateUser).not.toHaveBeenCalled();
    });

    it('asks for a branch, and then for a sub-branch when the branch has some, before it saves a member of the staff', async () => {
      const user = userEvent.setup();
      renderPage('/users/e1');
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Branch')).toHaveValue('d1'));
      await screen.findByLabelText('Sub-branch');

      await user.selectOptions(screen.getByLabelText('Branch'), '');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(screen.getByText('Please select a branch.')).toBeInTheDocument();

      await user.selectOptions(screen.getByLabelText('Branch'), 'd1');
      await screen.findByLabelText('Sub-branch');
      expect(screen.getByLabelText('Sub-branch')).toHaveValue(''); // changing the branch forgets the sub-branch
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(screen.getByText('Please select a sub-branch.')).toBeInTheDocument();
      expect(usersApi.updateUser).not.toHaveBeenCalled();

      await user.selectOptions(screen.getByLabelText('Sub-branch'), 's2');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await waitFor(() => expect(usersApi.updateUser).toHaveBeenCalled());
      expect(vi.mocked(usersApi.updateUser).mock.calls[0][1]).toMatchObject({ role: 'Employee', departmentId: 'd1', subBranchId: 's2', companyId: undefined });
    });

    it('does not ask for a sub-branch when the branch has none', async () => {
      const user = userEvent.setup();
      renderPage('/users/e1');
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Branch')).toHaveValue('d1'));

      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');

      await waitFor(() => expect(screen.queryByLabelText('Sub-branch')).not.toBeInTheDocument());
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await waitFor(() => expect(usersApi.updateUser).toHaveBeenCalled());
      expect(vi.mocked(usersApi.updateUser).mock.calls[0][1]).toMatchObject({ departmentId: 'd2', subBranchId: undefined });
    });

    it('does not send the company of a client who becomes a member of the staff, nor the branch of a member of the staff who becomes a client', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Company')).toHaveValue('co1'));
      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');
      await user.selectOptions(screen.getByLabelText('Branch'), 'd2');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await waitFor(() => expect(usersApi.updateUser).toHaveBeenCalledTimes(1));
      expect(vi.mocked(usersApi.updateUser).mock.calls[0][1]).toMatchObject({ role: 'Employee', departmentId: 'd2', companyId: undefined });

      const second = renderPage('/users/e1');
      await waitFor(() => expect(within(second.container).getAllByRole('button', { name: /Edit account details/ }).length).toBeGreaterThan(0));
      await user.click(within(second.container).getAllByRole('button', { name: /Edit account details/ }).at(-1)!);
      await waitFor(() => expect(within(second.container).getAllByLabelText('Branch').at(-1)).toHaveValue('d1'));
      await user.selectOptions(within(second.container).getAllByLabelText('Account type').at(-1)!, 'Client');
      await user.selectOptions(within(second.container).getAllByLabelText('Company').at(-1)!, 'co2');
      await user.click(within(second.container).getAllByRole('button', { name: 'Save' }).at(-1)!);

      await waitFor(() => expect(usersApi.updateUser).toHaveBeenCalledTimes(2));
      expect(vi.mocked(usersApi.updateUser).mock.calls[1][1]).toMatchObject({ role: 'Client', companyId: 'co2', departmentId: undefined, subBranchId: undefined });
    });

    it('takes the message about what is missing away as soon as the form is sent again', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Company')).toHaveValue('co1'));
      await user.selectOptions(screen.getByLabelText('Company'), '');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(screen.getByText('Please select a company.')).toBeInTheDocument();
      vi.mocked(usersApi.updateUser).mockReturnValue(new Promise(() => {}) as never);

      await user.selectOptions(screen.getByLabelText('Company'), 'co2');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(screen.queryByText('Please select a company.')).not.toBeInTheDocument();
    });

    it('shows the fields of the account type that is chosen', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      expect(screen.getByLabelText('Company')).toBeInTheDocument();
      expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument();

      await user.selectOptions(screen.getByLabelText('Account type'), 'Employee');

      expect(screen.getByLabelText('Branch')).toBeInTheDocument();
      expect(screen.queryByLabelText('Company')).not.toBeInTheDocument();
      await user.selectOptions(screen.getByLabelText('Account type'), 'Admin');
      expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Company')).not.toBeInTheDocument();
    });

    it('says what the server said when it refuses, and plainly when it said nothing, and stays open', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Company')).toHaveValue('co1'));
      vi.mocked(usersApi.updateUser).mockRejectedValueOnce({ response: { data: { message: 'The last administrator cannot be demoted.' } } }).mockRejectedValueOnce(new Error('network'));

      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('The last administrator cannot be demoted.')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('Could not save changes.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    });

    it('cannot be saved twice at once', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      await waitFor(() => expect(screen.getByLabelText('Company')).toHaveValue('co1'));
      const pending = deferred<undefined>();
      vi.mocked(usersApi.updateUser).mockReturnValue(pending.promise as never);

      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
      await act(async () => pending.resolve(undefined));
    });

    it('forgets what was typed when it is closed and opened again', async () => {
      const user = userEvent.setup();
      renderPage();
      await open(user);
      await user.clear(screen.getByLabelText('First name'));
      await user.type(screen.getByLabelText('First name'), 'Changed');
      await user.click(screen.getByRole('button', { name: 'Cancel' }));

      await open(user);

      expect(screen.getByLabelText('First name')).toHaveValue('Kemal');
    });
  });

  describe('switching the account off and on', () => {
    it('asks before it switches an account off, switches it off, and shows that it is', async () => {
      const user = userEvent.setup();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Deactivate account' }));
      expect(within(dialog()).getByText('Deactivate this account?')).toBeInTheDocument();
      expect(within(dialog()).getByText(/Kemal Klijent will no longer be able to log in/)).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
      expect(usersApi.setActive).not.toHaveBeenCalled();

      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, isActive: false });
      await user.click(screen.getByRole('button', { name: 'Deactivate account' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));

      await waitFor(() => expect(field('Status')).toHaveTextContent('Inactive'));
      expect(vi.mocked(usersApi.setActive).mock.calls).toEqual([['u1', false]]);
      expect(screen.getByRole('button', { name: 'Reactivate account' })).toBeInTheDocument();
    });

    it('switches an account on without asking', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, isActive: false });
      renderPage();
      vi.mocked(usersApi.getById).mockResolvedValue(client);

      await user.click(await screen.findByRole('button', { name: 'Reactivate account' }));

      await waitFor(() => expect(field('Status')).toHaveTextContent('Active'));
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(vi.mocked(usersApi.setActive).mock.calls).toEqual([['u1', true]]);
    });
  });

  describe('a new temporary password', () => {
    it('is asked for after a warning, and shown with when it expires', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.regenerateTempPassword).mockResolvedValue({ userId: 'u1', email: 'kemal@firma.test', temporaryPassword: 'Tmp-Pass-123', temporaryPasswordExpiresAtUtc: new Date(Date.now() + 20 * 60_000).toISOString() });
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Regenerate temp password' }));
      expect(within(dialog()).getByText('Regenerate temporary password?')).toBeInTheDocument();
      expect(within(dialog()).getByText(/immediately invalidates Kemal Klijent's current password/)).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
      expect(usersApi.regenerateTempPassword).not.toHaveBeenCalled();

      await user.click(screen.getByRole('button', { name: 'Regenerate temp password' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Regenerate' }));

      expect(await screen.findByText('Tmp-Pass-123')).toBeInTheDocument();
      expect(vi.mocked(usersApi.regenerateTempPassword).mock.calls).toEqual([['u1']]);
      await user.click(screen.getByRole('button', { name: 'Done' }));
      expect(screen.queryByText('Tmp-Pass-123')).not.toBeInTheDocument();
    });

    it('is not shown when the server says it was only e-mailed to the owner', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.regenerateTempPassword).mockResolvedValue({ userId: 'u1', email: 'kemal@firma.test', temporaryPassword: null, temporaryPasswordExpiresAtUtc: new Date(Date.now() + 20 * 60_000).toISOString() });
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Regenerate temp password' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Regenerate' }));

      expect(await screen.findByText('The password was sent only to the account owner by email.')).toBeInTheDocument();
    });
  });

  describe('the personal data', () => {
    it('is exported as a file named after the person', async () => {
      const user = userEvent.setup();
      const blob = new Blob(['{}'], { type: 'application/json' });
      vi.mocked(usersApi.exportData).mockResolvedValue(blob);
      const created = vi.fn(() => 'blob:export');
      const revoked = vi.fn();
      Object.assign(URL, { createObjectURL: created, revokeObjectURL: revoked });
      const downloads: { name: string; href: string }[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        downloads.push({ name: this.download, href: this.href });
      });
      renderPage();

      await user.click(await screen.findByRole('button', { name: 'Export personal data' }));

      await waitFor(() => expect(downloads).toEqual([{ name: 'user-u1.json', href: 'blob:export' }]));
      expect(created).toHaveBeenCalledWith(blob);
      expect(revoked).toHaveBeenCalledWith('blob:export');
      expect(vi.mocked(usersApi.exportData).mock.calls).toEqual([['u1']]);
    });

    it('says so when it could not be exported', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.exportData).mockRejectedValue(new Error('network'));
      renderPage();

      await user.click(await screen.findByRole('button', { name: 'Export personal data' }));

      expect(await screen.findByText('The data could not be exported.')).toBeInTheDocument();
    });

    it('is anonymized only after a question that says what is lost and what is kept', async () => {
      const user = userEvent.setup();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Anonymize…' }));
      expect(within(dialog()).getByText('Anonymize this person?')).toBeInTheDocument();
      expect(within(dialog()).getByText(/removed for good and the account locked/)).toBeInTheDocument();
      expect(within(dialog()).getByText(/Obrisan korisnik/)).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
      expect(usersApi.anonymize).not.toHaveBeenCalled();

      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, firstName: 'Obrisan', lastName: 'korisnik', email: 'x@anonymized.invalid' });
      await user.click(screen.getByRole('button', { name: 'Anonymize…' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Anonymize' }));

      expect(await screen.findByRole('heading', { name: 'Obrisan korisnik' })).toBeInTheDocument();
      expect(vi.mocked(usersApi.anonymize).mock.calls).toEqual([['u1']]);
      expect(screen.queryByRole('button', { name: 'Anonymize…' })).not.toBeInTheDocument();
    });

    it('says so when it could not be anonymized', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.anonymize).mockRejectedValue(new Error('network'));
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Anonymize…' }));

      await user.click(within(dialog()).getByRole('button', { name: 'Anonymize' }));

      expect(await screen.findByText('The account could not be anonymized.')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('takes the message away when the next try goes through', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.exportData).mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(new Blob(['{}']));
      Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() });
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Export personal data' }));
      await screen.findByText('The data could not be exported.');

      await user.click(screen.getByRole('button', { name: 'Export personal data' }));

      await waitFor(() => expect(screen.queryByText('The data could not be exported.')).not.toBeInTheDocument());
    });
  });

  describe('the picture', () => {
    const fileInput = () => document.querySelector('input[type="file"]') as HTMLInputElement;

    it('is uploaded, and the page asks for the person again', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByRole('button', { name: 'Change' });
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, profilePictureUrl: '/pictures/new.png' });

      await user.upload(fileInput(), new File(['x'], 'me.png', { type: 'image/png' }));

      await waitFor(() => expect(document.querySelector('img')?.getAttribute('src')).toBe('/pictures/new.png'));
      expect(vi.mocked(usersApi.uploadProfilePicture).mock.calls[0][0]).toBe('u1');
      expect(vi.mocked(usersApi.uploadProfilePicture).mock.calls[0][1].name).toBe('me.png');
    });

    it('says what the server said when it refuses it, and plainly when it said nothing', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByRole('button', { name: 'Change' });
      vi.mocked(usersApi.uploadProfilePicture).mockRejectedValueOnce({ response: { data: { message: 'The picture is too large.' } } }).mockRejectedValueOnce(new Error('network'));

      await user.upload(fileInput(), new File(['x'], 'big.png', { type: 'image/png' }));
      expect(await screen.findByText('The picture is too large.')).toBeInTheDocument();
      await user.upload(fileInput(), new File(['x'], 'again.png', { type: 'image/png' }));
      expect(await screen.findByText('Could not upload the picture.')).toBeInTheDocument();
    });
  });

  // What the page used to get wrong, or to leave unsaid.
  describe('when something goes wrong', () => {
    it('shows the next user after a failed load of the previous one, and not "User not found." for everybody who follows', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.getById).mockImplementation(async (id) => {
        if (id === 'u1') throw { response: { status: 404 } };
        return employee;
      });
      renderPage('/users/u1');
      expect(await screen.findByText('User not found.')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'go to /users/e1' }));

      expect(await screen.findByRole('heading', { name: 'Amra Agent' })).toBeInTheDocument();
      expect(screen.queryByText('User not found.')).not.toBeInTheDocument();
    });

    it.each([
      { what: 'cannot be reached', error: new Error('network') },
      { what: 'fails', error: { response: { status: 500 } } },
    ])('says that the user could not be loaded, and not that they were not found, when the server $what', async ({ error }) => {
      vi.mocked(usersApi.getById).mockRejectedValue(error);

      renderPage();

      expect(await screen.findByText(/The user could not be loaded/)).toBeInTheDocument();
      expect(screen.queryByText('User not found.')).not.toBeInTheDocument();
    });

    it('shows the user who is open now, however late the answer for the previous one arrives', async () => {
      const user = userEvent.setup();
      const slow = deferred<UserListItemDto>();
      vi.mocked(usersApi.getById).mockImplementation((id) => (id === 'u1' ? slow.promise : Promise.resolve(employee)));
      renderPage('/users/u1');

      await user.click(screen.getByRole('button', { name: 'go to /users/e1' }));
      expect(await screen.findByRole('heading', { name: 'Amra Agent' })).toBeInTheDocument();
      await act(async () => slow.resolve(client));

      expect(screen.getByRole('heading', { name: 'Amra Agent' })).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Kemal Klijent' })).not.toBeInTheDocument();
    });

    it('shows the tickets of the user who is open now, however late the tickets of the previous one arrive', async () => {
      const user = userEvent.setup();
      const slow = deferred<ReturnType<typeof page>>();
      vi.mocked(ticketsApi.list).mockImplementation(((params: { clientId?: string }) => (params.clientId === 'u1' ? slow.promise : Promise.resolve(page([ticket('t9', '200009', 'Za Amru')])))) as never);
      renderPage('/users/u1');
      await screen.findByRole('heading', { name: 'Kemal Klijent' });

      await user.click(screen.getByRole('button', { name: 'go to /users/e1' }));
      expect(await screen.findByText('Za Amru')).toBeInTheDocument();
      await act(async () => slow.resolve(page([ticket('t1', '100001', 'Printer ne radi')])));

      expect(screen.getByText('Za Amru')).toBeInTheDocument();
      expect(screen.queryByText('Printer ne radi')).not.toBeInTheDocument();
    });

    it('shows the tickets of the other client, and not of the previous one, when one client is followed by another', async () => {
      const user = userEvent.setup();
      vi.mocked(ticketsApi.list).mockImplementation((async (params: { clientId?: string }) => page([ticket(`t-${params.clientId}`, `N-${params.clientId}`, `Za ${params.clientId}`)])) as never);
      renderPage('/users/u1');
      expect(await screen.findByText('Za u1')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'go to /users/u2' }));

      expect(await screen.findByText('Za u2')).toBeInTheDocument();
      expect(screen.queryByText('Za u1')).not.toBeInTheDocument();
    });

    it('does not show a person or their tickets from memory when the page is left and opened again', async () => {
      const keeping = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const first = renderWithQuery(<MemoryRouter initialEntries={['/users/u1']}><Routes><Route path="/users/:id" element={<UserDetailPage />} /></Routes></MemoryRouter>, keeping);
      await screen.findByText('Printer ne radi');
      first.unmount();
      await new Promise((r) => setTimeout(r, 10));
      vi.mocked(usersApi.getById).mockReturnValue(new Promise(() => {}));
      vi.mocked(ticketsApi.list).mockReturnValue(new Promise(() => {}));

      renderWithQuery(<MemoryRouter initialEntries={['/users/u1']}><Routes><Route path="/users/:id" element={<UserDetailPage />} /></Routes></MemoryRouter>, keeping);

      expect(screen.queryByRole('heading', { name: 'Kemal Klijent' })).not.toBeInTheDocument();
      expect(document.querySelector('svg.animate-spin')).toBeInTheDocument();
    });

    it('asks for the tickets again after the account was changed, because who is listed and under which name can change', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, isActive: false });
      renderPage();
      await screen.findByText('Printer ne radi');
      expect(ticketsApi.list).toHaveBeenCalledTimes(1);

      await user.click(await screen.findByRole('button', { name: 'Reactivate account' }));

      await waitFor(() => expect(ticketsApi.list).toHaveBeenCalledTimes(2));
    });

    it('says that the tickets could not be loaded, instead of saying there are none', async () => {
      vi.mocked(ticketsApi.list).mockRejectedValue(new Error('network'));

      renderPage();

      expect(await screen.findByText(/The tickets could not be loaded/)).toBeInTheDocument();
      expect(screen.queryByText('No tickets found.')).not.toBeInTheDocument();
    });

    it('says how many tickets there are and that only the first of them are shown, when there are more than the page asks for', async () => {
      vi.mocked(ticketsApi.list).mockResolvedValue({ items: [ticket('t1', '100001', 'Printer ne radi'), ticket('t2', '100002', 'Skener')], totalCount: 250, page: 1, pageSize: 100 } as never);

      renderPage();

      expect(await screen.findByText('Tickets submitted (250)')).toBeInTheDocument();
      expect(screen.getByText('Showing the first 2.')).toBeInTheDocument();
    });

    it('does not say that only the first are shown when all of them are', async () => {
      renderPage();

      await screen.findByText('Printer ne radi');

      expect(screen.queryByText(/Showing the first/)).not.toBeInTheDocument();
    });

    it.each([
      { what: 'switching an account off', start: async (user: ReturnType<typeof userEvent.setup>) => {
        await user.click(await screen.findByRole('button', { name: 'Deactivate account' }));
        await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));
      } },
    ])('says why $what failed, in the words of the server, and plainly when it said nothing', async ({ start }) => {
      const user = userEvent.setup();
      vi.mocked(usersApi.setActive).mockRejectedValueOnce({ response: { data: { message: 'The last administrator cannot be switched off.' } } }).mockRejectedValueOnce(new Error('network'));
      renderPage();

      await start(user);
      expect(await screen.findByText('The last administrator cannot be switched off.')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Deactivate account' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));
      expect(await screen.findByText('Could not change the account.')).toBeInTheDocument();
    });

    it('says why switching an account on failed', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, isActive: false });
      vi.mocked(usersApi.setActive).mockRejectedValue(new Error('network'));
      renderPage();

      await user.click(await screen.findByRole('button', { name: 'Reactivate account' }));

      expect(await screen.findByText('Could not change the account.')).toBeInTheDocument();
      expect(field('Status')).toHaveTextContent('Inactive');
    });

    it('says why a new temporary password could not be made, in the words of the server, and plainly when it said nothing', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.regenerateTempPassword).mockRejectedValueOnce({ response: { data: { message: 'The account is not active.' } } }).mockRejectedValueOnce(new Error('network'));
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Regenerate temp password' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Regenerate' }));

      expect(await screen.findByText('The account is not active.')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Regenerate temp password' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Regenerate' }));
      expect(await screen.findByText('Could not generate a new password.')).toBeInTheDocument();
    });

    it('keeps showing the person while it asks for them again after a change, instead of replacing the page with a spinner', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.getById).mockResolvedValue({ ...client, isActive: false });
      renderPage();
      const reactivate = await screen.findByRole('button', { name: 'Reactivate account' });
      vi.mocked(usersApi.getById).mockReturnValue(new Promise(() => {})); // the next time the person is asked for, the answer does not come
      await user.click(reactivate);
      await waitFor(() => expect(usersApi.setActive).toHaveBeenCalled());
      await waitFor(() => expect(usersApi.getById).toHaveBeenCalledTimes(2));

      expect(screen.getByRole('heading', { name: 'Kemal Klijent' })).toBeInTheDocument();
      expect(document.querySelector('svg.animate-spin')).not.toBeInTheDocument();
    });

    it('keeps the form for editing out of reach of the keyboard and of a screen reader while it is closed', async () => {
      const user = userEvent.setup();
      renderPage();
      await user.click(await screen.findByRole('button', { name: /Edit account details/ }));
      expect(screen.getByRole('textbox', { name: 'First name' })).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /Close/ }));

      expect(screen.queryByRole('textbox', { name: 'First name' })).not.toBeInTheDocument();
      expect(screen.getByLabelText('First name').closest('[inert]')).not.toBeNull();
    });
  });
});
