import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { WorkTimeEditor } from './WorkTimeEditor';
import { installFakeHttp, refuse } from '../../test/fakeHttp';
import type { TicketDetailDto, TicketWorkSessionDto } from '../../types';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));

/** Now is Monday, 12 October 2026, 16:00. A moment is told in local time, as the person at the keyboard sees it. */
const NOW = new Date(2026, 9, 12, 16, 0, 0);
const at = (h: number, m: number) => new Date(2026, 9, 12, h, m, 0).toISOString();

const session = (id: string, from: [number, number], to: [number, number] | null, userName = 'Amra Agent'): TicketWorkSessionDto => ({
  id, userId: 'u1', userName, startedAtUtc: at(...from), endedAtUtc: to ? at(...to) : null,
});

let state: { sessions: TicketWorkSessionDto[]; assigned: boolean };
let server: ReturnType<typeof setup>;

/** The server as far as the editor knows it: the sessions of one ticket, and the rule that an end has to be after its start. */
function setup(sessions: TicketWorkSessionDto[], assigned = true) {
  state = { sessions: sessions.map((s) => ({ ...s })), assigned };
  const ticketNow = () => ({ id: 't1', assignedToUserId: state.assigned ? 'u1' : null, assignedToName: 'Amra Agent', workSessions: state.sessions }) as unknown as TicketDetailDto;
  return installFakeHttp((method, url, body) => {
    const one = url.match(/^\/api\/tickets\/t1\/work-sessions(?:\/([^/]+))?$/);
    if (!one) return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
    if (method === 'POST' || method === 'PUT') {
      if (body.endedAtUtc && new Date(body.endedAtUtc) <= new Date(body.startedAtUtc)) refuse(400, { message: 'The end of a work session must be after its start.' });
      if (method === 'POST') state.sessions.push({ id: `n${state.sessions.length + 1}`, userId: 'u1', userName: 'Amra Agent', startedAtUtc: body.startedAtUtc, endedAtUtc: body.endedAtUtc });
      else Object.assign(state.sessions.find((s) => s.id === one[1])!, { startedAtUtc: body.startedAtUtc, endedAtUtc: body.endedAtUtc });
      return ticketNow();
    }
    if (method === 'DELETE') {
      state.sessions = state.sessions.filter((s) => s.id !== one[1]);
      return ticketNow();
    }
    return refuse(404, { message: 'No.' });
  });
}

function Harness({ initial }: { initial: TicketDetailDto }) {
  const [ticket, setTicket] = useState(initial);
  return <WorkTimeEditor ticket={ticket} onChanged={setTicket} />;
}

function renderEditor(sessions: TicketWorkSessionDto[] = [], assigned = true, before?: (s: ReturnType<typeof setup>) => void) {
  server = setup(sessions, assigned);
  before?.(server);
  const initial = { id: 't1', assignedToUserId: assigned ? 'u1' : null, assignedToName: 'Amra Agent', workSessions: state.sessions } as unknown as TicketDetailDto;
  return { user: userEvent.setup(), ...render(<Harness initial={initial} />) };
}

const sent = (method: string) => server.sent(method, /work-sessions/);
const dateField = (label: string) => screen.getByLabelText(label) as HTMLInputElement;
const timeField = (label: string) => screen.getByLabelText(label) as HTMLInputElement;
const setDate = (label: string, value: string) => fireEvent.change(dateField(label), { target: { value } });
async function setTime(user: ReturnType<typeof userEvent.setup>, label: string, value: string) {
  await user.clear(timeField(label));
  if (value) await user.type(timeField(label), value);
  await user.tab();
}
const byName = (name: string | RegExp) => screen.getByRole('button', { name });

afterEach(() => vi.useRealTimers());
const clockAt = (extra: string[] = []) => {
  vi.useFakeTimers({ toFake: ['Date', ...extra] as never[] });
  vi.setSystemTime(NOW);
};

describe('the work time of a ticket', () => {
  describe('what it shows', () => {
    it('says that nothing was logged, and has no total', () => {
      clockAt();
      renderEditor();

      expect(screen.getByText('No work logged yet.')).toBeInTheDocument();
      expect(screen.queryByText(/^Total:/)).not.toBeInTheDocument();
    });

    it('lists the sessions with who, from when to when, and for how long, and the total of them', () => {
      clockAt();
      renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [13, 0], [13, 45], 'Šime Šimić')]);

      expect(screen.getByText('Amra Agent')).toBeInTheDocument();
      expect(screen.getByText('12.10.2026 09:00 – 12.10.2026 10:30')).toBeInTheDocument();
      expect(screen.getByText('1h 30min')).toBeInTheDocument();
      expect(screen.getByText('Šime Šimić')).toBeInTheDocument();
      expect(screen.getByText('45min')).toBeInTheDocument();
      expect(screen.getByText('Total: 2h 15min')).toBeInTheDocument();
    });

    it('says that a session is running, counts it up to now, and offers to end only that one', () => {
      clockAt();
      renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [15, 40], null)]);

      expect(within(screen.getAllByRole('listitem')[0]).queryByRole('button', { name: /^End now/ })).not.toBeInTheDocument();
      const running = screen.getAllByRole('listitem')[1];
      expect(within(running).getByText('running')).toBeInTheDocument();
      expect(within(running).getByText('20min')).toBeInTheDocument();
      expect(within(running).getByRole('button', { name: /^End now/ })).toBeInTheDocument();
      expect(screen.getByText('Total: 1h 50min')).toBeInTheDocument();
    });

    it('does not offer the form, and says why, when nobody is assigned to the ticket', () => {
      clockAt();
      renderEditor([], false);

      expect(screen.getByText('Assign the ticket to somebody to log work time on it.')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Add session' })).not.toBeInTheDocument();
    });

    it('says whose work it is that is logged', () => {
      clockAt();
      renderEditor();

      expect(screen.getByText('Log work by Amra Agent')).toBeInTheDocument();
    });
  });

  describe('adding a session', () => {
    it('sends what was entered, as the moments they are, and clears the form', async () => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', '09:15');
      setDate('Ended date', '2026-10-12');
      await setTime(user, 'Ended time', '10:45');
      expect(screen.getByText('Duration: 1h 30min')).toBeInTheDocument();

      await user.click(byName('Add session'));

      expect(sent('POST')).toHaveLength(1);
      expect(sent('POST')[0].body).toEqual({ startedAtUtc: at(9, 15), endedAtUtc: at(10, 45) });
      expect(await screen.findByText('12.10.2026 09:15 – 12.10.2026 10:45')).toBeInTheDocument();
      expect(dateField('Started date').value).toBe('');
      expect(timeField('Started time').value).toBe('');
      expect(screen.queryByText(/^Duration:/)).not.toBeInTheDocument();
    });

    it('sends a session that has no end, which is still going', async () => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', '15:30');

      await user.click(byName('Add session'));

      expect(sent('POST')[0].body).toEqual({ startedAtUtc: at(15, 30), endedAtUtc: null });
      expect(await screen.findByText('running')).toBeInTheDocument();
    });

    it.each([
      { what: 'nothing', date: '', time: '' },
      { what: 'a date without a time', date: '2026-10-12', time: '' },
      { what: 'a time without a date', date: '', time: '09:00' },
    ])('says that the start is needed when there is $what, and sends nothing', async ({ date, time }) => {
      clockAt();
      const { user } = renderEditor();
      if (date) setDate('Started date', date);
      if (time) await setTime(user, 'Started time', time);

      await user.click(byName('Add session'));

      expect(await screen.findByRole('alert')).toHaveTextContent('Enter when the work started.');
      expect(sent('POST')).toHaveLength(0);
    });

    it('takes the time that is being typed when the button is pressed straight from the field', async () => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await user.type(timeField('Started time'), '0845');

      await user.click(byName('Add session'));

      expect(sent('POST')[0].body).toEqual({ startedAtUtc: at(8, 45), endedAtUtc: null });
    });

    it('says what the server said when it refuses, and keeps what was entered', async () => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', '10:00');
      setDate('Ended date', '2026-10-12');
      await setTime(user, 'Ended time', '09:00');
      expect(screen.queryByText(/^Duration:/)).not.toBeInTheDocument();

      await user.click(byName('Add session'));

      expect(await screen.findByRole('alert')).toHaveTextContent('The end of a work session must be after its start.');
      expect(timeField('Started time').value).toBe('10:00');
      expect(dateField('Ended date').value).toBe('2026-10-12');
    });

    it('says so in its own words when the server said nothing', async () => {
      clockAt();
      const { user } = renderEditor([], true, (s) => s.refuse('POST', /work-sessions/, 500));
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', '10:00');

      await user.click(byName('Add session'));

      expect(await screen.findByRole('alert')).toHaveTextContent('Could not save the work session.');
    });

    it('says that it is saving, and cannot be pressed again, until the server has answered', async () => {
      clockAt();
      const { user } = renderEditor([], true, (s) => s.hold('POST', /work-sessions/));
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', '10:00');

      await user.click(byName('Add session'));

      expect(byName('Saving…')).toBeDisabled();
    });
  });

  describe('the time fields', () => {
    it.each([
      { typed: '9', shown: '09:00' },
      { typed: '14', shown: '14:00' },
      { typed: '9:30', shown: '09:30' },
      { typed: '0930', shown: '09:30' },
      { typed: '1430', shown: '14:30' },
      { typed: '14:30', shown: '14:30' },
      { typed: ' 7:05 ', shown: '07:05' },
    ])('writes $typed as $shown', async ({ typed, shown }) => {
      clockAt();
      const { user } = renderEditor();

      await setTime(user, 'Started time', typed);

      expect(timeField('Started time').value).toBe(shown);
    });

    it('clears when the field is emptied', async () => {
      clockAt();
      const { user } = renderEditor();
      await setTime(user, 'Started time', '10:00');

      await setTime(user, 'Started time', '');

      expect(timeField('Started time').value).toBe('');
    });
  });

  describe('editing and ending and deleting', () => {
    it('puts a session in the form to be changed, saves it, and puts the form back', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30])]);

      await user.click(byName(/^Edit/));

      expect(screen.getByText('Change this session')).toBeInTheDocument();
      expect(dateField('Started date').value).toBe('2026-10-12');
      expect(timeField('Started time').value).toBe('09:00');
      expect(dateField('Ended date').value).toBe('2026-10-12');
      expect(timeField('Ended time').value).toBe('10:30');
      await setTime(user, 'Ended time', '11:00');
      await user.click(byName('Save changes'));

      expect(sent('PUT')).toHaveLength(1);
      expect(sent('PUT')[0].url).toBe('/api/tickets/t1/work-sessions/s1');
      expect(sent('PUT')[0].body).toEqual({ startedAtUtc: at(9, 0), endedAtUtc: at(11, 0) });
      expect(await screen.findByText('12.10.2026 09:00 – 12.10.2026 11:00')).toBeInTheDocument();
      expect(screen.getByText('Log work by Amra Agent')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    });

    it('lets the change be given up, and then the form is empty and adds again', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30])]);
      await user.click(byName(/^Edit/));

      await user.click(byName('Cancel'));

      expect(screen.getByRole('button', { name: 'Add session' })).toBeInTheDocument();
      expect(dateField('Started date').value).toBe('');
      expect(timeField('Ended time').value).toBe('');
    });

    it('ends a running session now, with the start it had', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [15, 40], null)]);

      await user.click(byName(/^End now/));

      expect(sent('PUT')).toHaveLength(1);
      expect(sent('PUT')[0].body).toEqual({ startedAtUtc: at(15, 40), endedAtUtc: NOW.toISOString() });
      expect(await screen.findByText('12.10.2026 15:40 – 12.10.2026 16:00')).toBeInTheDocument();
    });

    it('says what the server said when it cannot end a session, and in its own words when it said nothing', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [15, 40], null)], true, (s) => s.refuse('PUT', /work-sessions/, 500));

      await user.click(byName(/^End now/));

      expect(await screen.findByRole('alert')).toHaveTextContent('Could not end the work session.');
    });

    it('deletes a session after asking, and does nothing when it is not confirmed', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [13, 0], [13, 45])]);

      await user.click(within(screen.getAllByRole('listitem')[0]).getByRole('button', { name: /^Delete/ }));
      const dialog = screen.getByRole('dialog');
      expect(within(dialog).getByText('Delete this work session?')).toBeInTheDocument();
      expect(within(dialog).getByText(/Amra Agent, 12\.10\.2026 09:00\. This cannot be undone\./)).toBeInTheDocument();
      await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      expect(sent('DELETE')).toHaveLength(0);

      await user.click(within(screen.getAllByRole('listitem')[0]).getByRole('button', { name: /^Delete/ }));
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(sent('DELETE')).toHaveLength(1);
      expect(sent('DELETE')[0].url).toBe('/api/tickets/t1/work-sessions/s1');
      expect(await screen.findByText('Total: 45min')).toBeInTheDocument();
    });

    it('says why a session could not be deleted, and leaves it', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30])], true, (s) => s.refuse('DELETE', /work-sessions/, 500));

      await user.click(byName(/^Delete/));
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('Could not delete the work session.');
      expect(screen.getByText('Amra Agent')).toBeInTheDocument();
    });

    it('empties the form when the session that is being changed is deleted', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30])]);
      await user.click(byName(/^Edit/));

      await user.click(byName(/^Delete/));
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByText('No work logged yet.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Add session' })).toBeInTheDocument();
      expect(dateField('Started date').value).toBe('');
    });
  });

  describe('what was wrong', () => {
    it.each([
      { what: 'an end date without a time', date: '2026-10-12', time: '' },
      { what: 'an end time without a date', date: '', time: '10:00' },
    ])('does not take $what for a session that is still going', async ({ date, time }) => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', '09:00');
      if (date) setDate('Ended date', date);
      if (time) await setTime(user, 'Ended time', time);

      await user.click(byName('Add session'));

      expect(await screen.findByRole('alert')).toHaveTextContent('Enter both the date and the time when the work ended, or leave both empty if it is still going.');
      expect(sent('POST')).toHaveLength(0);
    });

    it.each([
      { typed: '930', shown: '09:30' },
      { typed: '1230', shown: '12:30' },
    ])('reads $typed as hours and minutes, not as hours', async ({ typed, shown }) => {
      clockAt();
      const { user } = renderEditor();

      await setTime(user, 'Started time', typed);

      expect(timeField('Started time').value).toBe(shown);
    });

    it.each(['25:00', '12:75', '12:60', '24', '99', '9:5', 'ab', '12:30:00'])('does not change %s into another time, but says that it is not one', async (typed) => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', typed);

      expect(timeField('Started time')).toHaveAttribute('aria-invalid', 'true');
      expect(timeField('Started time').value).toBe(typed);
      await user.click(byName('Add session'));

      expect(await screen.findByRole('alert')).toHaveTextContent('Enter the time as hours and minutes, for example 09:30.');
      expect(sent('POST')).toHaveLength(0);
    });

    it('does not flag a time that is fine, or one that is not there', async () => {
      clockAt();
      const { user } = renderEditor();

      await setTime(user, 'Started time', '9:30');

      expect(timeField('Started time')).not.toHaveAttribute('aria-invalid', 'true');
      expect(timeField('Ended time')).not.toHaveAttribute('aria-invalid', 'true');
    });

    it('says the time of a session to the nearest minute', () => {
      clockAt();
      const exact = { ...session('s1', [9, 0], [10, 29]), endedAtUtc: new Date(2026, 9, 12, 10, 29, 40).toISOString() };
      renderEditor([exact]);

      expect(screen.getByText('1h 30min')).toBeInTheDocument();
    });

    it('says that an end time is not a time, as it says it of a start time', async () => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await setTime(user, 'Started time', '09:00');
      setDate('Ended date', '2026-10-12');
      await setTime(user, 'Ended time', '25:00');
      expect(timeField('Ended time')).toHaveAttribute('aria-invalid', 'true');

      await user.click(byName('Add session'));

      expect(await screen.findByRole('alert')).toHaveTextContent('Enter the time as hours and minutes, for example 09:30.');
      expect(sent('POST')).toHaveLength(0);
    });

    it('stops saying why the last action was refused when a session is picked to be changed', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [15, 40], null)], true, (s) => s.refuse('PUT', /work-sessions/, 500));
      await user.click(byName(/^End now/));
      expect(await screen.findByRole('alert')).toBeInTheDocument();

      await user.click(byName(/^Edit/));

      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('is not sent by the browser itself, which would leave the page', async () => {
      clockAt();
      renderEditor();

      const notPrevented = fireEvent.submit(screen.getByRole('button', { name: 'Add session' }).closest('form')!);

      expect(notPrevented).toBe(false);
    });

    it('closes the question about deleting when it is answered, whatever the server says', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30])], true, (s) => s.refuse('DELETE', /work-sessions/, 500));

      await user.click(byName(/^Delete/));
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByRole('alert')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('keeps the form of the session that is being changed when another session is deleted', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [13, 0], [13, 45])]);
      await user.click(within(screen.getAllByRole('listitem')[0]).getByRole('button', { name: /^Edit/ }));

      await user.click(within(screen.getAllByRole('listitem')[1]).getByRole('button', { name: /^Delete/ }));
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByText('Total: 1h 30min')).toBeInTheDocument();
      expect(screen.getByText('Change this session')).toBeInTheDocument();
      expect(timeField('Started time').value).toBe('09:00');
    });

    it('keeps the form of the session that is being changed when its deletion is refused', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30])], true, (s) => s.refuse('DELETE', /work-sessions/, 500));
      await user.click(byName(/^Edit/));

      await user.click(byName(/^Delete/));
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByRole('alert')).toBeInTheDocument();
      expect(screen.getByText('Change this session')).toBeInTheDocument();
      expect(timeField('Started time').value).toBe('09:00');
    });

    it('cannot be pressed anywhere while something is being saved', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [15, 40], null)], true, (s) => s.hold('PUT', /work-sessions/));
      await user.click(within(screen.getAllByRole('listitem')[0]).getByRole('button', { name: /^Edit/ }));
      await setTime(user, 'Ended time', '11:00');

      await user.click(byName('Save changes'));

      expect(byName('Saving…')).toBeDisabled();
      expect(byName('Cancel')).toBeDisabled();
      for (const name of [/^Edit/, /^Delete/, /^End now/]) {
        for (const button of screen.getAllByRole('button', { name })) expect(button).toBeDisabled();
      }
    });

    it('keeps counting a session that is running: its time and the total go on while the page is open', async () => {
      clockAt(['setInterval', 'clearInterval']);
      renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [15, 40], null)]);
      expect(screen.getByText('20min')).toBeInTheDocument();
      expect(screen.getByText('Total: 1h 50min')).toBeInTheDocument();

      await act(async () => { vi.advanceTimersByTime(5 * 60 * 1000); });

      expect(screen.getByText('25min')).toBeInTheDocument();
      expect(screen.getByText('Total: 1h 55min')).toBeInTheDocument();
    });

    it('does not count by itself when no session is running', async () => {
      clockAt(['setInterval', 'clearInterval']);
      const spy = vi.spyOn(globalThis, 'setInterval');
      renderEditor([session('s1', [9, 0], [10, 30])]);

      expect(spy).not.toHaveBeenCalled();
      spy.mockRestore();
    });

    it('stops counting when the session is ended', async () => {
      clockAt(['setInterval', 'clearInterval']);
      const user = userEvent.setup({ advanceTimers: (ms) => vi.advanceTimersByTime(ms) });
      server = setup([session('s1', [15, 40], null)]);
      const initial = { id: 't1', assignedToUserId: 'u1', assignedToName: 'Amra Agent', workSessions: state.sessions } as unknown as TicketDetailDto;
      render(<Harness initial={initial} />);

      await user.click(byName(/^End now/));
      await screen.findByText('12.10.2026 15:40 – 12.10.2026 16:00');
      await act(async () => { vi.advanceTimersByTime(10 * 60 * 1000); });

      expect(screen.getByText('20min')).toBeInTheDocument();
      expect(screen.getByText('Total: 20min')).toBeInTheDocument();
    });

    it('names every button after the session it is for, so that a list of them can be told apart', () => {
      clockAt();
      renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [15, 40], null, 'Šime Šimić')]);

      expect(screen.getByRole('button', { name: 'Edit the session of Amra Agent from 12.10.2026 09:00' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Delete the session of Amra Agent from 12.10.2026 09:00' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Edit the session of Šime Šimić from 12.10.2026 15:40' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'End now: the session of Šime Šimić from 12.10.2026 15:40' })).toBeInTheDocument();
    });

    it('saves when Enter is pressed in a field, taking the time that was just typed', async () => {
      clockAt();
      const { user } = renderEditor();
      setDate('Started date', '2026-10-12');
      await user.type(timeField('Started time'), '0845{Enter}');

      expect(sent('POST')).toHaveLength(1);
      expect(sent('POST')[0].body).toEqual({ startedAtUtc: at(8, 45), endedAtUtc: null });
    });

    it('stops saying why the last save was refused when the change is given up', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30])]);
      await user.click(byName(/^Edit/));
      await setTime(user, 'Ended time', '08:00');
      await user.click(byName('Save changes'));
      expect(await screen.findByRole('alert')).toBeInTheDocument();

      await user.click(byName('Cancel'));

      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('empties the form when the session that is being changed has been removed by somebody else', async () => {
      clockAt();
      const { user } = renderEditor([session('s1', [9, 0], [10, 30]), session('s2', [13, 0], [13, 45])]);
      await user.click(within(screen.getAllByRole('listitem')[0]).getByRole('button', { name: /^Edit/ }));
      expect(screen.getByText('Change this session')).toBeInTheDocument();

      // the other session is deleted, which gives the page a ticket without the one that was being changed
      await user.click(within(screen.getAllByRole('listitem')[1]).getByRole('button', { name: /^Delete/ }));
      state.sessions = state.sessions.filter((s) => s.id !== 's1');
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByText('No work logged yet.')).toBeInTheDocument();
      expect(screen.queryByText('Change this session')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Add session' })).toBeInTheDocument();
    });
  });
});
