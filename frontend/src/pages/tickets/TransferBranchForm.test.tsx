import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TransferBranchForm } from './TransferBranchForm';
import { lookupsApi } from '../../api/lookups';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto } from '../../types';

vi.mock('../../api/lookups', () => ({ lookupsApi: { departments: vi.fn(), subBranches: vi.fn() } }));
vi.mock('../../api/tickets', () => ({ ticketsApi: { transferBranch: vi.fn() } }));

const ticket = { id: 't1', departmentId: 'a' } as unknown as TicketDetailDto;

// NOVO-DOM-02: a branch that has sub-branches needs one chosen when a ticket is moved into it.
describe('moving a ticket to another branch', () => {
  beforeEach(() => {
    vi.mocked(lookupsApi.departments).mockResolvedValue([
      { id: 'a', name: 'Branch A' },
      { id: 'b', name: 'Branch B' },
      { id: 'c', name: 'Branch C' },
    ] as never);
    vi.mocked(lookupsApi.subBranches).mockReset().mockImplementation(async (departmentId: string) =>
      (departmentId === 'b' ? [{ id: 'sb', departmentId: 'b', name: 'Sub of B', isActive: true }] : []) as never
    );
    vi.mocked(ticketsApi.transferBranch).mockReset().mockResolvedValue({ id: 't1' } as never);
  });

  async function open() {
    render(<TransferBranchForm ticket={ticket} onTransferred={() => {}} />);
    await userEvent.click(screen.getByRole('button', { name: /Transfer to another branch/ }));
  }

  it('asks for a sub-branch when the destination has some, and sends it', async () => {
    await open();
    await userEvent.selectOptions(await screen.findByDisplayValue('Select destination branch…'), 'b');

    await userEvent.click(screen.getByRole('button', { name: 'Transfer' }));
    expect(await screen.findByText(/Select a sub-branch/, { selector: 'p, div, span' })).toBeInTheDocument();
    expect(ticketsApi.transferBranch).not.toHaveBeenCalled();

    await userEvent.selectOptions(await screen.findByLabelText('Destination sub-branch'), 'sb');
    await userEvent.click(screen.getByRole('button', { name: 'Transfer' }));

    expect(ticketsApi.transferBranch).toHaveBeenCalledWith('t1', 'b', 'sb');
  });

  it('asks for nothing more when the destination has no sub-branches', async () => {
    await open();
    await userEvent.selectOptions(await screen.findByDisplayValue('Select destination branch…'), 'c');

    await userEvent.click(screen.getByRole('button', { name: 'Transfer' }));

    expect(screen.queryByLabelText('Destination sub-branch')).not.toBeInTheDocument();
    expect(ticketsApi.transferBranch).toHaveBeenCalledWith('t1', 'c', undefined);
  });
});
