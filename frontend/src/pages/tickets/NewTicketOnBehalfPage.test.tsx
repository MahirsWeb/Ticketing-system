import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import NewTicketOnBehalfPage from './NewTicketOnBehalfPage';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { UserSummary } from '../../types';

vi.mock('../../api/lookups', () => ({
  lookupsApi: { helpTopics: vi.fn(), departments: vi.fn(), slaPlans: vi.fn(), ticketCategories: vi.fn(), subBranches: vi.fn() },
}));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn(), lookupByEmail: vi.fn() } }));
vi.mock('../../api/tickets', () => ({ ticketsApi: { createOnBehalf: vi.fn() } }));
vi.mock('../../api/emailIntegration', () => ({ emailIntegrationApi: { getPrefill: vi.fn(), completeTicket: vi.fn() } }));

const admin = { id: 'admin', firstName: 'Ad', lastName: 'Min', role: 'Admin' } as UserSummary;

// T-17: the branch, its sub-branches and the lists come from the shared cache now. Picking an assignee still has to carry the page to the
// assignee's branch and sub-branch, with the sub-branch chosen only when that branch's list has arrived.
describe('opening a ticket on behalf of a client', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 't', user: admin });
    vi.mocked(lookupsApi.helpTopics).mockResolvedValue([{ id: 'topic', name: 'Topic' }] as never);
    vi.mocked(lookupsApi.departments).mockResolvedValue([
      { id: 'a', name: 'Branch A', email: 'a@x.test', isActive: true },
      { id: 'b', name: 'Branch B', email: 'b@x.test', isActive: true },
    ] as never);
    vi.mocked(lookupsApi.slaPlans).mockResolvedValue([{ id: 'sla', name: 'Standard' }] as never);
    vi.mocked(lookupsApi.ticketCategories).mockResolvedValue([] as never);
    vi.mocked(lookupsApi.subBranches).mockReset().mockImplementation(
      async (departmentId: string) => (departmentId === 'b' ? [{ id: 'sb', departmentId: 'b', name: 'Sub of B', isActive: true }] : []) as never
    );
    vi.mocked(usersApi.list).mockReset().mockResolvedValue([
      { id: 'e1', firstName: 'Berina', lastName: 'Berić', role: 'Employee', departmentId: 'b', subBranchId: 'sb' },
      { id: 'e2', firstName: 'Amra', lastName: 'Amić', role: 'Employee', departmentId: 'a', subBranchId: null },
    ] as never);
  });

  // The form appears once a client has been found.
  async function open() {
    vi.mocked(usersApi.lookupByEmail).mockResolvedValue({ id: 'c1', firstName: 'Klijent', lastName: 'Klijentić', email: 'k@x.test', phoneNumber: null, companyId: 'co', companyName: 'Acme' } as never);
    renderWithQuery(
      <MemoryRouter initialEntries={['/tickets/new-on-behalf']}>
        <NewTicketOnBehalfPage />
      </MemoryRouter>
    );
    await userEvent.type(screen.getByPlaceholderText('client@company.com'), 'k@x.test');
    await userEvent.click(screen.getByRole('button', { name: 'Find client' }));
  }

  it('takes the first entry of each list as the starting choice, once the lists have arrived', async () => {
    await open();

    expect(await screen.findByDisplayValue('Branch A')).toBeInTheDocument(); // the admin's first branch
    expect(screen.getByDisplayValue('Topic')).toBeInTheDocument();
    expect(screen.getByDisplayValue(/Standard/)).toBeInTheDocument(); // the option also shows the hours
  });

  it("carries the page to the assignee's branch and sub-branch, and drops the sub-branch for a branch that has none", async () => {
    await open();
    await screen.findByDisplayValue('Branch A');
    await screen.findByRole('option', { name: 'Berina Berić' });

    await userEvent.selectOptions(screen.getByLabelText('Assign to'), 'e1');

    expect(await screen.findByDisplayValue('Branch B')).toBeInTheDocument();
    expect(await screen.findByLabelText('Sub-branch')).toHaveValue('sb');

    await userEvent.selectOptions(screen.getByLabelText('Assign to'), 'e2');

    expect(await screen.findByDisplayValue('Branch A')).toBeInTheDocument();
    expect(screen.queryByLabelText('Sub-branch')).not.toBeInTheDocument();
  });
});
