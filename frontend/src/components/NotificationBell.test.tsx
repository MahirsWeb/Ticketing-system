import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { NotificationBell } from './NotificationBell';
import { renderWithQuery } from '../test/renderWithQuery';
import { installFakeHttp, refuse } from '../test/fakeHttp';
import type { NotificationDto } from '../api/notifications';

vi.mock('../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn() } }));

/** The live connection, as far as the bell knows it: what it listens for, and what it does when the connection comes back. */
const hub = vi.hoisted(() => ({
  handlers: new Map<string, () => void>(),
  reconnected: [] as (() => void)[],
  removed: [] as string[],
}));
vi.mock('../lib/notificationsHub', () => ({
  getNotificationsHub: () => ({
    on: (event: string, handler: () => void) => hub.handlers.set(event, handler),
    off: (event: string) => {
      hub.handlers.delete(event);
      hub.removed.push(event);
    },
    onreconnected: (handler: () => void) => hub.reconnected.push(handler),
  }),
}));

const EVENTS = ['newTicket', 'ticketOpened', 'ticketAssigned', 'ticketClosed', 'newResponse', 'userActivatedViaInvite', 'ticketTransferred', 'ticketOverdue', 'taskAssigned', 'ticketUnassigned'];

const hoursAgo = (h: number) => new Date(Date.now() - h * 3600 * 1000).toISOString();
const note = (id: string, isRead: boolean, extra: Partial<NotificationDto> = {}): NotificationDto => ({
  id, type: 'NewTicket', message: `Message ${id}`, ticketId: null, isRead, createdAt: hoursAgo(2), ...extra,
});

let server: ReturnType<typeof setup>;

/** The server as far as the bell knows it: the latest notifications, as many as are asked for, and the ones that have been read. */
function setup(notes: NotificationDto[]) {
  const state = { notes: notes.map((n) => ({ ...n })) };
  const http = installFakeHttp((method, url, _body, params) => {
    if (method === 'GET' && url === '/api/notifications') return state.notes.slice(0, params?.take ?? 20);
    const read = url.match(/^\/api\/notifications\/([^/]+)\/read$/);
    if (method === 'POST' && read) {
      const found = state.notes.find((n) => n.id === read[1]) ?? refuse(404, { message: 'No such notification.' });
      found.isRead = true;
      return null;
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  });
  return { state, ...http };
}

function renderBell(notes: NotificationDto[] = [], before?: (server: ReturnType<typeof setup>) => void) {
  server = setup(notes);
  before?.(server);
  return renderWithQuery(
    <MemoryRouter initialEntries={['/']}>
      <NotificationBell />
      <Routes>
        <Route path="/" element={<p>home</p>} />
        <Route path="/tickets/:id" element={<p>the ticket</p>} />
      </Routes>
    </MemoryRouter>
  );
}

const listRequests = () => server.sent('GET', /^\/api\/notifications$/);
const bell = (name: string | RegExp = /^Notifications/) => screen.findByRole('button', { name });
const push = (event = 'newTicket') => act(async () => hub.handlers.get(event)!());

describe('the notification bell', () => {
  beforeEach(() => {
    hub.handlers.clear();
    hub.reconnected.length = 0;
    hub.removed.length = 0;
  });

  describe('the button', () => {
    it('has a name that says how many notifications are unread (TS-46.5)', async () => {
      renderBell([note('1', false), note('2', false), note('3', true)]);

      expect(await bell('Notifications, 2 unread')).toBeInTheDocument();
    });

    it('has a plain name when everything has been read, and does not read the emoji aloud', async () => {
      renderBell([note('1', true)]);

      const button = await bell('Notifications');
      expect(button.querySelector('[aria-hidden]')).toHaveTextContent('🔔');
    });

    it('says whether the list is open', async () => {
      renderBell([]);
      const button = await bell('Notifications');

      expect(button).toHaveAttribute('aria-expanded', 'false');
      await userEvent.click(button);
      expect(button).toHaveAttribute('aria-expanded', 'true');
    });

    it('shows how many are unread as a badge, as "9+" when there are more than nine, and not at all when there are none', async () => {
      const many = Array.from({ length: 12 }, (_, i) => note(`n${i}`, false));
      const first = renderBell(many);
      expect(await bell('Notifications, 12 unread')).toHaveTextContent('9+');
      first.unmount();

      renderBell([note('1', false), note('2', false)]);
      expect(await bell('Notifications, 2 unread')).toHaveTextContent('2');
    });
  });

  describe('the list', () => {
    it('shows the latest notifications with when they came, the unread ones in bolder type, and asks for twenty', async () => {
      const user = userEvent.setup();
      renderBell([note('1', false), note('2', true, { createdAt: hoursAgo(30) })]);
      await user.click(await bell(/^Notifications/));

      const first = await screen.findByText('Message 1');
      expect(listRequests()[0].params).toEqual({ take: 20 });
      expect(first.closest('button')).toHaveTextContent('about 2 hours ago');
      expect(first.closest('button')).toHaveClass('font-medium');
      expect(screen.getByText('Message 2').closest('button')).toHaveTextContent('1 day ago');
      expect(screen.getByText('Message 2').closest('button')).not.toHaveClass('font-medium');
    });

    it('says that there are none when there are none', async () => {
      const user = userEvent.setup();
      renderBell([]);
      await user.click(await bell('Notifications'));

      expect(await screen.findByText('No notifications yet.')).toBeInTheDocument();
    });

    it('closes when something outside it is pressed', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)]);
      const button = await bell('Notifications');
      await user.click(button);
      expect(await screen.findByText('Message 1')).toBeInTheDocument();

      await user.click(screen.getByText('home'));

      expect(screen.queryByText('Message 1')).not.toBeInTheDocument();
      expect(button).toHaveAttribute('aria-expanded', 'false');
    });
  });

  describe('what is pushed', () => {
    it('listens for every kind of event, and asks for the list again when one comes', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)]);
      await user.click(await bell('Notifications'));
      await screen.findByText('Message 1');
      expect([...hub.handlers.keys()].sort()).toEqual([...EVENTS].sort());
      server.state.notes.unshift(note('2', false));

      await push('ticketAssigned');

      expect(await screen.findByText('Message 2')).toBeInTheDocument();
      expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
    });

    it('asks for the list again once the connection is back, since what was pushed while it was down was missed', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)]);
      await user.click(await bell('Notifications'));
      await screen.findByText('Message 1');
      server.state.notes.unshift(note('2', false));
      expect(hub.reconnected).toHaveLength(1);

      await act(async () => hub.reconnected[0]());

      expect(await screen.findByText('Message 2')).toBeInTheDocument();
    });

    it('stops listening when it goes away, also for the connection coming back, which a connection cannot be told to forget', async () => {
      const view = renderBell([note('1', true)]);
      await bell('Notifications');
      const asked = listRequests().length;

      view.unmount();
      await act(async () => hub.reconnected[0]());
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });

      expect([...hub.removed].sort()).toEqual([...EVENTS].sort());
      expect(listRequests()).toHaveLength(asked);
    });
  });

  // NOVO-FE-10: marking a notification read failed, and the click then did nothing at all.
  describe('when a notification is clicked', () => {
    const forTicket = (id: string, isRead: boolean) => note(id, isRead, { ticketId: 'ticket-9' });

    it('opens the ticket even when marking it as read fails, and shows it as still unread', async () => {
      renderBell([forTicket('1', false)], (s) => s.refuse('POST', /read$/, 500, {}));

      await userEvent.click(await bell('Notifications, 1 unread'));
      await userEvent.click(await screen.findByText('Message 1'));

      expect(await screen.findByText('the ticket')).toBeInTheDocument();
      expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
    });

    it('marks it read and opens the ticket when everything works', async () => {
      renderBell([forTicket('1', false)]);

      await userEvent.click(await bell('Notifications, 1 unread'));
      await userEvent.click(await screen.findByText('Message 1'));

      expect(await screen.findByText('the ticket')).toBeInTheDocument();
      expect(server.sent('POST', /1\/read$/)).toHaveLength(1);
      expect(await bell('Notifications')).toBeInTheDocument();
      // and the list is asked for again once the server has marked it, to be sure that it is what the server says
      await waitFor(() => expect(listRequests()).toHaveLength(2));
    });

    it('shows it as read at once, without waiting for the server', async () => {
      const user = userEvent.setup();
      renderBell([forTicket('1', false)]);
      await user.click(await bell('Notifications, 1 unread'));
      const marking = server.hold('POST', /1\/read$/);

      await user.click(await screen.findByText('Message 1'));

      expect(await bell('Notifications')).toBeInTheDocument();
      marking.release();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });
    });

    it('closes the list, and opens no ticket when the notification is not about one', async () => {
      renderBell([note('1', false)]);

      await userEvent.click(await bell('Notifications, 1 unread'));
      await userEvent.click(await screen.findByText('Message 1'));

      expect(screen.queryByText('Message 1')).not.toBeInTheDocument();
      expect(screen.getByText('home')).toBeInTheDocument();
      expect(await bell('Notifications')).toBeInTheDocument();
    });

    it('opens the ticket, and does not tell the server anything, when the notification was read already', async () => {
      renderBell([forTicket('1', true)]);

      await userEvent.click(await bell('Notifications'));
      await userEvent.click(await screen.findByText('Message 1'));

      expect(await screen.findByText('the ticket')).toBeInTheDocument();
      expect(server.sent('POST', /read$/)).toHaveLength(0);
    });
  });

  describe('what it used to get wrong', () => {
    it('says that the notifications are on their way, and not that there are none, until they have come', async () => {
      const user = userEvent.setup();
      const held = { release: () => {} };
      renderBell([note('1', true)], (s) => Object.assign(held, s.hold('GET', /^\/api\/notifications$/)));

      await user.click(await bell('Notifications'));

      expect(screen.getByText('Loading notifications…')).toBeInTheDocument();
      expect(screen.queryByText('No notifications yet.')).not.toBeInTheDocument();
      held.release();
      expect(await screen.findByText('Message 1')).toBeInTheDocument();
      expect(screen.queryByText('Loading notifications…')).not.toBeInTheDocument();
    });

    it('says that the notifications could not be loaded, and offers to try again, instead of saying nothing and showing none', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)], (s) => s.refuse('GET', /^\/api\/notifications$/, 500, {}));
      await user.click(await bell('Notifications'));

      expect(await screen.findByText('The notifications could not be loaded.')).toBeInTheDocument();
      expect(screen.queryByText('No notifications yet.')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Try again' }));
      expect(await screen.findByText('Message 1')).toBeInTheDocument();
      expect(screen.queryByText('The notifications could not be loaded.')).not.toBeInTheDocument();
    });

    it('keeps what it has, and says that it could not be brought up to date, when only a later load fails', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)]);
      await user.click(await bell('Notifications'));
      await screen.findByText('Message 1');
      server.refuse('GET', /^\/api\/notifications$/, 500, {});

      await push();

      expect(await screen.findByText('The notifications could not be loaded.')).toBeInTheDocument();
      expect(screen.getByText('Message 1')).toBeInTheDocument();
    });

    it('shows the answer to the latest request even when the answer to an earlier one arrives last', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)]);
      await user.click(await bell('Notifications'));
      await screen.findByText('Message 1');
      const early = server.hold('GET', /^\/api\/notifications$/);
      await push(); // asked, and the answer is held: it is what the server said before the next one came
      server.state.notes.unshift(note('2', false));

      await push(); // the answer to this one comes at once, and is the newer
      expect(await screen.findByText('Message 2')).toBeInTheDocument();
      early.release();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });

      expect(screen.getByText('Message 2')).toBeInTheDocument();
      expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
    });

    it('does not show a notification as unread for good when an answer that was asked for before the click arrives after it', async () => {
      const user = userEvent.setup();
      renderBell([note('1', false, { ticketId: 'ticket-9' })]);
      await user.click(await bell('Notifications, 1 unread'));
      await screen.findByText('Message 1');
      const stale = server.hold('GET', /^\/api\/notifications$/);
      await push(); // asked, and the answer, which says unread, is held
      await user.click(screen.getByText('Message 1'));
      await screen.findByText('the ticket');

      stale.release();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });

      expect(await bell('Notifications')).toBeInTheDocument();
    });

    it('closes when Escape is pressed, and gives the focus back to the button from wherever in the list it was', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)]);
      const button = await bell('Notifications');
      await user.click(button);
      await screen.findByText('Message 1');
      await user.tab();
      expect(screen.getByText('Message 1').closest('button')).toHaveFocus();

      await user.keyboard('{Escape}');

      expect(screen.queryByText('Message 1')).not.toBeInTheDocument();
      expect(button).toHaveFocus();
    });

    it('closes when the focus moves away from it, for the keyboard is not a mouse', async () => {
      const user = userEvent.setup();
      renderBell([note('1', false)]);
      await user.click(await bell('Notifications, 1 unread'));
      await screen.findByText('Message 1');

      const elsewhere = document.body.appendChild(Object.assign(document.createElement('button'), { textContent: 'elsewhere' }));
      try {
        act(() => elsewhere.focus());

        await waitFor(() => expect(screen.queryByText('Message 1')).not.toBeInTheDocument());
      } finally {
        elsewhere.remove();
      }
    });

    it('does not close when the focus moves inside it', async () => {
      const user = userEvent.setup();
      renderBell([note('1', false), note('2', false)]);
      await user.click(await bell('Notifications, 2 unread'));
      await screen.findByText('Message 1');

      await user.tab();

      expect(screen.getByText('Message 1')).toBeInTheDocument();
    });

    it('names the list, so that a screen reader can tell what opened', async () => {
      const user = userEvent.setup();
      renderBell([note('1', true)]);
      await user.click(await bell('Notifications'));

      expect(await screen.findByRole('group', { name: 'Notifications' })).toBeInTheDocument();
      expect(within(screen.getByRole('group', { name: 'Notifications' })).getByText('Message 1')).toBeInTheDocument();
    });
  });
});
