import { useState } from 'react';
import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TicketAssigneesPanel } from './TicketAssigneesPanel';
import { queryClient } from '../../api/queryClient';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installFakeHttp, refuse } from '../../test/fakeHttp';
import type { TicketDetailDto } from '../../types';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), delete: vi.fn() } }));

const people = [
  { id: 'u1', firstName: 'Amra', lastName: 'Ahmetović', role: 'Employee', departmentId: 'd1', subBranchId: null },
  { id: 'u2', firstName: 'Bego', lastName: 'Begić', role: 'Employee', departmentId: 'd1', subBranchId: null },
  { id: 'u3', firstName: 'Cana', lastName: 'Čaušević', role: 'Employee', departmentId: 'd1', subBranchId: 's1' },
  { id: 'u4', firstName: 'Dino', lastName: 'Dinić', role: 'Employee', departmentId: 'd2', subBranchId: null },
];

const ticketOf = (extra: Partial<TicketDetailDto> = {}) =>
  ({ id: 't1', departmentId: 'd1', subBranchId: null, assignedToUserId: 'u1', assignees: [{ userId: 'u1', name: 'Amra Ahmetović' }], ...extra }) as unknown as TicketDetailDto;

let server: ReturnType<typeof setup>;
const originalDefaults = queryClient.getDefaultOptions();

/** The server as far as the panel knows it: who can be asked, and the assignees of the ticket, which only it changes. */
function setup(ticket: TicketDetailDto) {
  const state = { ticket: { ...ticket, assignees: [...ticket.assignees] } };
  const http = installFakeHttp((method, url, body, params) => {
    if (method === 'GET' && url === '/api/users') {
      return people.filter((p) => (!params?.role || p.role === params.role) && (!params?.departmentId || p.departmentId === params.departmentId) && (!params?.subBranchId || p.subBranchId === params.subBranchId));
    }
    if (method === 'POST' && url === `/api/tickets/${state.ticket.id}/assignees`) {
      const who = people.find((p) => p.id === body.userId) ?? refuse(400, { message: 'That person does not exist.' });
      if (state.ticket.assignees.some((a) => a.userId === who.id)) refuse(400, { message: 'That person is already on the ticket.' });
      state.ticket.assignees.push({ userId: who.id, name: `${who.firstName} ${who.lastName}` });
      return state.ticket;
    }
    const removal = url.match(new RegExp(`^/api/tickets/${state.ticket.id}/assignees/([^/]+)$`));
    if (method === 'DELETE' && removal) {
      if (removal[1] === state.ticket.assignedToUserId) refuse(400, { message: 'The person who works on the ticket cannot be removed.' });
      state.ticket.assignees = state.ticket.assignees.filter((a) => a.userId !== removal[1]);
      return state.ticket;
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  });
  return { state, ...http };
}

/** The page keeps the ticket and hands changes back to it, as the real one does. */
function Harness({ start, onChanged }: { start: TicketDetailDto; onChanged?: (t: TicketDetailDto) => void }) {
  const [ticket, setTicket] = useState(start);
  return (
    <>
      <button onClick={() => setTicket(ticketOf({ id: 't2', departmentId: 'd2', assignedToUserId: 'u4', assignees: [{ userId: 'u4', name: 'Dino Dinić' }] }))}>show the other ticket</button>
      <TicketAssigneesPanel
        ticket={ticket}
        onChanged={(t) => {
          setTicket(t);
          onChanged?.(t);
        }}
      />
    </>
  );
}

function renderPanel(ticket: TicketDetailDto = ticketOf(), before?: (server: ReturnType<typeof setup>) => void, onChanged?: (t: TicketDetailDto) => void) {
  server = setup(ticket);
  before?.(server);
  return renderWithQuery(<Harness start={ticket} onChanged={onChanged} />, queryClient);
}

const userRequests = () => server.sent('GET', /^\/api\/users$/);
const addSelect = () => screen.getByRole('combobox');
/** The choice of who to add is only there once there is somebody to choose; this waits for it, and for the person. */
const optionReady = async (name: string) => within(await screen.findByRole('combobox')).findByRole('option', { name });

describe('the assignees of a ticket', () => {
  beforeEach(() => {
    queryClient.clear();
    queryClient.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false } });
  });
  afterAll(() => queryClient.setDefaultOptions(originalDefaults));

  describe('who is on the ticket', () => {
    it('says who works on it and who helps, and that nobody has it when nobody does', async () => {
      renderPanel(ticketOf({ assignees: [{ userId: 'u1', name: 'Amra Ahmetović' }, { userId: 'u2', name: 'Bego Begić' }] }));

      expect(await screen.findByText('Amra Ahmetović')).toBeInTheDocument();
      expect(screen.getByText('works on it')).toBeInTheDocument();
      expect(screen.getByText('helps')).toBeInTheDocument();
      expect(screen.getByText(/Only the person who works on the ticket logs time/)).toBeInTheDocument();
    });

    it('says "Unassigned" when nobody is on the ticket, and has no note about time', async () => {
      renderPanel(ticketOf({ assignedToUserId: null as never, assignees: [] }));

      expect(await screen.findByText('Unassigned')).toBeInTheDocument();
      expect(screen.queryByText(/Only the person who works on the ticket logs time/)).not.toBeInTheDocument();
    });
  });

  describe('who can be added', () => {
    it('are the employees of the branch of the ticket, except those who are on it already', async () => {
      renderPanel();

      await optionReady('Bego Begić');
      expect(userRequests()[0].params).toEqual({ role: 'Employee', departmentId: 'd1', subBranchId: undefined });
      expect(within(addSelect()).getAllByRole('option').map((o) => o.textContent)).toEqual(['Add a co-assignee…', 'Bego Begić', 'Cana Čaušević']);
    });

    it('are the employees of the sub-branch when the ticket is in one', async () => {
      renderPanel(ticketOf({ subBranchId: 's1' }));

      await optionReady('Cana Čaušević');
      expect(userRequests()[0].params).toEqual({ role: 'Employee', departmentId: 'd1', subBranchId: 's1' });
      expect(within(addSelect()).getAllByRole('option').map((o) => o.textContent)).toEqual(['Add a co-assignee…', 'Cana Čaušević']);
    });

    it('are not asked for, and nothing is offered, when the ticket is in no branch', async () => {
      renderPanel(ticketOf({ departmentId: null as never }));

      await screen.findByText('Amra Ahmetović');
      expect(userRequests()).toHaveLength(0);
      expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    });

    it('are not offered when everybody who could be is on the ticket already', async () => {
      renderPanel(ticketOf({ assignees: [{ userId: 'u1', name: 'Amra Ahmetović' }, { userId: 'u2', name: 'Bego Begić' }, { userId: 'u3', name: 'Cana Čaušević' }] }));

      await screen.findByText('Bego Begić');
      await waitFor(() => expect(userRequests()).toHaveLength(1));
      expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    });
  });

  describe('adding one', () => {
    it('adds the person that was chosen, hands the changed ticket back, and shows them among the assignees and no longer among those who can be added', async () => {
      const user = userEvent.setup();
      const onChanged = vi.fn();
      renderPanel(ticketOf(), undefined, onChanged);
      await optionReady('Bego Begić');

      await user.selectOptions(addSelect(), 'u2');
      await user.click(screen.getByRole('button', { name: 'Add' }));

      expect(await screen.findByText('helps')).toBeInTheDocument();
      expect(server.sent('POST', /assignees$/).map((r) => r.body)).toEqual([{ userId: 'u2' }]);
      expect(onChanged).toHaveBeenCalledTimes(1);
      expect(onChanged.mock.calls[0][0].assignees.map((a: { userId: string }) => a.userId)).toEqual(['u1', 'u2']);
      expect(within(addSelect()).queryByRole('option', { name: 'Bego Begić' })).not.toBeInTheDocument();
      expect(addSelect()).toHaveValue('');
      // and nobody is chosen any more, not only shown as not chosen: there is nothing to add until somebody is
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
    });

    it('cannot be pressed until somebody is chosen', async () => {
      renderPanel();
      await optionReady('Bego Begić');

      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
    });

    it('says what the server said when it refuses, or plainly when it said nothing, and keeps the choice', async () => {
      const user = userEvent.setup();
      renderPanel();
      await optionReady('Bego Begić');
      await user.selectOptions(addSelect(), 'u2');
      server.refuse('POST', /assignees$/, 400, { message: 'That person is on leave.' });

      await user.click(screen.getByRole('button', { name: 'Add' }));
      expect(await screen.findByText('That person is on leave.')).toBeInTheDocument();

      server.refuse('POST', /assignees$/, 500, {});
      await user.click(screen.getByRole('button', { name: 'Add' }));
      expect(await screen.findByText('Could not add assignee.')).toBeInTheDocument();
      expect(addSelect()).toHaveValue('u2');
    });

    it('cannot be pressed again, and the assignees cannot be removed, while it is being done', async () => {
      const user = userEvent.setup();
      const state = ticketOf({ assignees: [{ userId: 'u1', name: 'Amra Ahmetović' }, { userId: 'u3', name: 'Cana Čaušević' }] });
      renderPanel(state);
      await optionReady('Bego Begić');
      await user.selectOptions(addSelect(), 'u2');
      const hold = server.hold('POST', /assignees$/);

      await user.click(screen.getByRole('button', { name: 'Add' }));

      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
      screen.getAllByRole('button', { name: /×|Remove/ }).forEach((b) => expect(b).toBeDisabled());
      hold.release();
      await screen.findByText('Bego Begić', { selector: 'span' });
      expect(server.sent('POST', /assignees$/)).toHaveLength(1);
    });
  });

  describe('removing one', () => {
    it('removes the person whose × is pressed, hands the changed ticket back, and offers them to be added again', async () => {
      const user = userEvent.setup();
      const onChanged = vi.fn();
      renderPanel(ticketOf({ assignees: [{ userId: 'u1', name: 'Amra Ahmetović' }, { userId: 'u2', name: 'Bego Begić' }] }), undefined, onChanged);
      await screen.findByText('Bego Begić');

      const chip = screen.getByText('Bego Begić').closest('span')!;
      await user.click(within(chip).getByRole('button'));

      await waitFor(() => expect(screen.queryByText('helps')).not.toBeInTheDocument());
      expect(server.sent('DELETE', /assignees\/u2$/)).toHaveLength(1);
      expect(onChanged).toHaveBeenCalledTimes(1);
      await optionReady('Bego Begić');
    });

    it('says what the server said when it refuses, or plainly when it said nothing', async () => {
      const user = userEvent.setup();
      renderPanel();
      await screen.findByText('Amra Ahmetović');

      await user.click(within(screen.getByText('Amra Ahmetović').closest('span')!).getByRole('button'));
      expect(await screen.findByText('The person who works on the ticket cannot be removed.')).toBeInTheDocument();

      server.refuse('DELETE', /assignees\/u1$/, 500, {});
      await user.click(within(screen.getByText('Amra Ahmetović').closest('span')!).getByRole('button'));
      expect(await screen.findByText('Could not remove assignee.')).toBeInTheDocument();
    });
  });

  describe('what it used to get wrong', () => {
    it('says when the colleagues could not be loaded, since nobody could then be added and nothing would say why', async () => {
      renderPanel(ticketOf(), (s) => s.refuse('GET', /^\/api\/users$/, 500, {}, -1));

      expect(await screen.findByText('The colleagues could not be loaded, so nobody can be added now.')).toBeInTheDocument();
      expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    });

    it('does not say that when they could', async () => {
      renderPanel();
      await optionReady('Bego Begić');

      expect(screen.queryByText(/could not be loaded/)).not.toBeInTheDocument();
    });

    it('offers the colleagues of the branch the ticket is in now, even when the answer for the branch it was in before arrives last', async () => {
      const user = userEvent.setup();
      let early!: { release: () => void };
      renderPanel(ticketOf(), (s) => (early = s.hold('GET', /^\/api\/users$/)));

      await user.click(screen.getByRole('button', { name: 'show the other ticket' }));
      await screen.findByText('Dino Dinić');
      early.release();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });

      expect(screen.queryByRole('option', { name: 'Bego Begić' })).not.toBeInTheDocument();
      expect(userRequests().map((r) => (r.params as { departmentId: string }).departmentId)).toEqual(['d1', 'd2']);
    });

    it('names the button that removes a person after that person, and the choice of who to add', async () => {
      renderPanel(ticketOf({ assignees: [{ userId: 'u1', name: 'Amra Ahmetović' }, { userId: 'u2', name: 'Bego Begić' }] }));
      await screen.findByText('Bego Begić');

      expect(screen.getByRole('button', { name: 'Remove Bego Begić' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Remove Amra Ahmetović' })).toBeInTheDocument();
      expect(await screen.findByRole('combobox', { name: 'Add a co-assignee' })).toBeInTheDocument();
    });
  });
});
