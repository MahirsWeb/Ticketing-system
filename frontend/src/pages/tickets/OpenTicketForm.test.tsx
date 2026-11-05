import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OpenTicketForm } from './OpenTicketForm';
import { lookupsApi } from '../../api/lookups';
import { renderWithQuery } from '../../test/renderWithQuery';
import { usersApi } from '../../api/users';
import { ticketsApi } from '../../api/tickets';
import { useAuthStore } from '../../store/authStore';
import type { TicketDetailDto, UserSummary } from '../../types';

vi.mock('../../api/lookups', () => ({
  lookupsApi: { helpTopics: vi.fn(), departments: vi.fn(), slaPlans: vi.fn(), ticketCategories: vi.fn(), subBranches: vi.fn() },
}));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn() } }));
vi.mock('../../api/tickets', () => ({ ticketsApi: { open: vi.fn() } }));

const admin = { id: 'admin', firstName: 'Ad', lastName: 'Min', role: 'Admin' } as UserSummary;
const ticket = { id: 't1', departmentId: null, status: 'New' } as unknown as TicketDetailDto;

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const person = (id: string, firstName: string) => ({ id, firstName, lastName: 'Tester', role: 'Employee' }) as never;

// NOVO-FE-11: the agent changes the branch from A to B quickly. The answer for A arrives after the answer for B and used to replace it,
// so the list offered people of the wrong branch and the server then refused the one that was picked.
describe('the form that opens a ticket', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 't', user: admin });
    vi.mocked(lookupsApi.helpTopics).mockResolvedValue([{ id: 'topic', name: 'Topic' }] as never);
    vi.mocked(lookupsApi.departments).mockResolvedValue([
      { id: 'a', name: 'Branch A', email: 'a@x.test', isActive: true },
      { id: 'b', name: 'Branch B', email: 'b@x.test', isActive: true },
    ] as never);
    vi.mocked(lookupsApi.slaPlans).mockResolvedValue([{ id: 'sla', name: 'Standard' }] as never);
    vi.mocked(lookupsApi.ticketCategories).mockResolvedValue([]);
    vi.mocked(lookupsApi.subBranches).mockReset().mockResolvedValue([]);
    vi.mocked(usersApi.list).mockReset();
  });

  it('offers the people of the branch that is chosen now, whatever order the answers arrive in', async () => {
    const forA = deferred<never>();
    vi.mocked(usersApi.list).mockImplementation((params?: { departmentId?: string }) =>
      params?.departmentId === 'a' ? forA.promise : Promise.resolve([person('bea', 'Bea')])
    );
    renderWithQuery(<OpenTicketForm ticket={ticket} onOpened={() => {}} />);

    // Branch A is chosen first (the form picks the first one for an Admin); then the agent switches to B.
    const branch = await screen.findByDisplayValue('Branch A');
    await userEvent.selectOptions(branch, 'b');
    expect(await screen.findByRole('option', { name: 'Bea Tester' })).toBeInTheDocument();

    forA.resolve([person('ana', 'Ana')] as never); // the slower answer for A comes last
    await waitFor(() => expect(usersApi.list).toHaveBeenCalledTimes(2));
    await new Promise((resolve) => setTimeout(resolve, 20));

    const everyOption = screen.getAllByRole('option').map((o) => o.textContent);
    expect(everyOption).toContain('Bea Tester');
    expect(everyOption).not.toContain('Ana Tester');
  });

  it('does the same for the sub-branches', async () => {
    const forA = deferred<never>();
    vi.mocked(lookupsApi.subBranches).mockImplementation((departmentId: string) =>
      departmentId === 'a' ? forA.promise : Promise.resolve([{ id: 'sb', departmentId: 'b', name: 'Sub of B', isActive: true }] as never)
    );
    vi.mocked(usersApi.list).mockResolvedValue([]);
    renderWithQuery(<OpenTicketForm ticket={ticket} onOpened={() => {}} />);

    await userEvent.selectOptions(await screen.findByDisplayValue('Branch A'), 'b');
    expect(await screen.findByRole('option', { name: 'Sub of B' })).toBeInTheDocument();

    forA.resolve([{ id: 'sa', departmentId: 'a', name: 'Sub of A', isActive: true }] as never);
    await new Promise((resolve) => setTimeout(resolve, 20));

    const options = within(document.body).getAllByRole('option').map((o) => o.textContent);
    expect(options).toContain('Sub of B');
    expect(options).not.toContain('Sub of A');
  });

  describe('opening it', () => {
    beforeEach(() => {
      vi.mocked(lookupsApi.ticketCategories).mockResolvedValue([{ id: 'cat', name: 'Hardware' }] as never);
      vi.mocked(usersApi.list).mockResolvedValue([person('bea', 'Bea')]);
      vi.mocked(ticketsApi.open).mockReset().mockResolvedValue({ id: 't1', status: 'Open' } as never);
    });

    async function ready() {
      const onOpened = vi.fn();
      renderWithQuery(<OpenTicketForm ticket={ticket} onOpened={onOpened} />);
      await screen.findByDisplayValue('Branch A');
      await screen.findByRole('option', { name: 'Bea Tester' });
      return onOpened;
    }

    it('sends everything that was chosen, with the due date as the end of the day, and hands the opened ticket back', async () => {
      const user = userEvent.setup();
      const onOpened = await ready();
      await user.selectOptions(screen.getByLabelText('Assign to'), 'bea');
      await user.selectOptions(screen.getByLabelText('Category (internal, staff only)'), 'cat');
      fireEvent.change(screen.getByLabelText('Due date (optional)'), { target: { value: '2026-12-24' } });

      await user.click(screen.getByRole('button', { name: 'Open ticket' }));

      await waitFor(() => expect(onOpened).toHaveBeenCalledWith({ id: 't1', status: 'Open' }));
      expect(vi.mocked(ticketsApi.open).mock.calls).toEqual([
        ['t1', { source: 'TicketSystem', helpTopicId: 'topic', departmentId: 'a', subBranchId: undefined, slaPlanId: 'sla', priority: 'Medium', dueDateUtc: new Date('2026-12-24T23:59:59').toISOString(), assignedToUserId: 'bea', categoryId: 'cat' }],
      ]);
    });

    it('sends no due date and no category when none were chosen', async () => {
      const user = userEvent.setup();
      await ready();
      await user.selectOptions(screen.getByLabelText('Assign to'), 'bea');

      await user.click(screen.getByRole('button', { name: 'Open ticket' }));

      await waitFor(() => expect(ticketsApi.open).toHaveBeenCalled());
      expect(vi.mocked(ticketsApi.open).mock.calls[0][1]).toMatchObject({ dueDateUtc: null, categoryId: undefined });
    });

    it('wants somebody to give the ticket to', async () => {
      const user = userEvent.setup();
      await ready();

      await user.click(screen.getByRole('button', { name: 'Open ticket' }));

      expect(await screen.findByText('All fields except due date are required to open a ticket.')).toBeInTheDocument();
      expect(ticketsApi.open).not.toHaveBeenCalled();
    });

    it('wants a sub-branch when the branch has some', async () => {
      const user = userEvent.setup();
      vi.mocked(lookupsApi.subBranches).mockImplementation((async (departmentId: string) => (departmentId === 'b' ? [{ id: 'sb', departmentId: 'b', name: 'Sub of B', isActive: true }] : [])) as never);
      await ready();
      await user.selectOptions(screen.getByLabelText('Branch'), 'b');
      await screen.findByLabelText('Sub-branch');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'bea');

      await user.click(screen.getByRole('button', { name: 'Open ticket' }));

      expect(await screen.findByText('Please select a sub-branch.')).toBeInTheDocument();
      expect(ticketsApi.open).not.toHaveBeenCalled();
    });

    it('says what the server said when it refuses, and plainly when it said nothing', async () => {
      const user = userEvent.setup();
      vi.mocked(ticketsApi.open).mockRejectedValueOnce({ response: { data: { message: 'The ticket is already open.' } } }).mockRejectedValueOnce(new Error('network'));
      await ready();
      await user.selectOptions(screen.getByLabelText('Assign to'), 'bea');

      await user.click(screen.getByRole('button', { name: 'Open ticket' }));
      expect(await screen.findByText('The ticket is already open.')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Open ticket' }));
      expect(await screen.findByText('Could not open the ticket.')).toBeInTheDocument();
    });
  });
});
