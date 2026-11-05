import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { format } from 'date-fns';
import { MemoryRouter } from 'react-router-dom';
import EmailsPage from './EmailsPage';
import { queryClient } from '../../api/queryClient';
import { createPkce } from '../../lib/oauth';
import { redirectTo } from '../../lib/redirect';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installEmailBackend, message, newEmailState, type EmailState } from '../../test/fakeEmailApi';
import type { UserSummary } from '../../types';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../../lib/oauth', async (importOriginal) => ({ ...(await importOriginal<typeof import('../../lib/oauth')>()), createPkce: vi.fn() }));
vi.mock('../../lib/redirect', () => ({ redirectTo: vi.fn() }));

const employee = { id: 'u1', firstName: 'Amra', lastName: 'H', role: 'Employee', departmentId: 'branch-1', departmentName: 'Sarajevo' } as UserSummary;

let server: ReturnType<typeof installEmailBackend>;
const originalDefaults = queryClient.getDefaultOptions();

function renderPage(state: EmailState = newEmailState(), before?: (server: ReturnType<typeof installEmailBackend>) => void) {
  server = installEmailBackend(state);
  before?.(server);
  return renderWithQuery(
    <MemoryRouter>
      <EmailsPage />
    </MemoryRouter>,
    queryClient
  );
}

const connectButton = () => screen.findByRole('button', { name: /Connect branch mailbox/ });
const inboxRequests = () => server.sent('GET', /\/messages$/);
const rowOf = (subject: string) => screen.getByRole('button', { name: new RegExp(subject) }).closest('[role=presentation]') as HTMLElement;
const tab = (name: RegExp) => screen.getByText(name);

describe('the emails page', () => {
  beforeEach(() => {
    queryClient.clear();
    queryClient.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false } });
    useAuthStore.setState({ accessToken: 't', user: employee });
    vi.mocked(createPkce).mockReset().mockResolvedValue({ verifier: 'the-verifier', challenge: 'the-challenge' });
    vi.mocked(redirectTo).mockReset();
    sessionStorage.clear();
  });
  afterAll(() => queryClient.setDefaultOptions(originalDefaults));

  describe('before there is a mailbox to read', () => {
    it('tells somebody who is in no branch to ask for one, since the mailbox belongs to a branch', async () => {
      useAuthStore.setState({ accessToken: 't', user: { ...employee, departmentId: null } as UserSummary });
      renderPage();

      expect(await screen.findByText(/You're not assigned to a branch yet/)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /Connect branch mailbox/ })).not.toBeInTheDocument();
    });

    it('offers to connect the mailbox of the branch, and names the branch', async () => {
      renderPage({ ...newEmailState(), connected: false, connectedEmail: null });

      await connectButton();
      expect(screen.getByText(/Connect your branch's shared mailbox \(Sarajevo\)/)).toBeInTheDocument();
    });

    // NOVO-FE-08 / NOVO-FE-09: starting the connection to Microsoft.
    it('is refused, with a reason, where the browser cannot make the PKCE secret (a page that is not https)', async () => {
      vi.mocked(createPkce).mockResolvedValue(null);
      renderPage({ ...newEmailState(), connected: false, connectedEmail: null });

      await userEvent.click(await connectButton());

      expect(await screen.findByText(/secure connection \(https\)/)).toBeInTheDocument();
      expect(await connectButton()).toBeEnabled(); // and the button is usable again
      expect(redirectTo).not.toHaveBeenCalled();
    });

    it('puts the button back and says so when Microsoft\'s settings cannot be fetched', async () => {
      renderPage({ ...newEmailState(), connected: false, connectedEmail: null }, (s) => s.refuse('GET', /email-integration\/config$/, 500, {}));

      await userEvent.click(await connectButton());

      expect(await screen.findByText(/could not be started/)).toBeInTheDocument();
      expect(await connectButton()).toBeEnabled();
    });

    it('says that the integration is not set up when there is no client id', async () => {
      renderPage({ ...newEmailState(), connected: false, connectedEmail: null, clientId: '' });

      await userEvent.click(await connectButton());

      expect(await screen.findByText(/not configured yet \(missing Client ID\)/)).toBeInTheDocument();
      expect(redirectTo).not.toHaveBeenCalled();
    });

    it('goes to Microsoft with a state and a challenge, keeps the secret in this tab, and leaves the button as it is until the page is replaced', async () => {
      renderPage({ ...newEmailState(), connected: false, connectedEmail: null });

      await userEvent.click(await connectButton());

      await waitFor(() => expect(redirectTo).toHaveBeenCalledTimes(1));
      const url = new URL(vi.mocked(redirectTo).mock.calls[0][0]);
      expect(url.origin + url.pathname).toBe('https://login.microsoftonline.com/common/oauth2/v2.0/authorize');
      const sent = Object.fromEntries(url.searchParams);
      expect(sent).toMatchObject({
        client_id: 'client-id', response_type: 'code', redirect_uri: `${window.location.origin}/emails/callback`, response_mode: 'query',
        scope: 'offline_access Mail.Read User.Read', prompt: 'select_account', code_challenge: 'the-challenge', code_challenge_method: 'S256',
      });
      expect(sessionStorage.getItem('ms_oauth_state')).toBe(sent.state);
      expect(sent.state).not.toBe('');
      expect(sessionStorage.getItem('ms_oauth_verifier')).toBe('the-verifier');
      expect(screen.getByRole('button', { name: 'Redirecting…' })).toBeDisabled();
    });
  });

  describe('the inbox', () => {
    it('shows who the mailbox is, the latest 30 messages with who they are from, when, and whether they have attachments', async () => {
      renderPage();

      expect(await screen.findByText('Connected: sarajevo@firma.test')).toBeInTheDocument();
      await screen.findByText('Printer ne radi');
      expect(inboxRequests()[0].params).toEqual({ take: 30 });
      expect(rowOf('Printer ne radi')).toHaveTextContent('Mia Marić');
      expect(within(rowOf('Printer ne radi')).getByRole('img', { name: 'Has attachments' })).toBeInTheDocument();
      expect(rowOf('Treba mi pristup')).toHaveTextContent('m2@klijent.test'); // no name: the address
      expect(rowOf('Treba mi pristup')).toHaveTextContent('about 2 hours ago');
      expect(within(rowOf('Treba mi pristup')).queryByRole('img')).not.toBeInTheDocument();
    });

    it('counts the marked messages in the tab, and shows only those when it is chosen', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      expect(tab(/^Marked for tickets/)).toHaveTextContent('Marked for tickets (2)');
      expect(screen.queryByText('No emails found.')).not.toBeInTheDocument();

      await user.click(tab(/^Marked for tickets/));

      expect(screen.queryByText('Printer ne radi')).not.toBeInTheDocument();
      expect(screen.queryByText('No emails marked as tickets yet.')).not.toBeInTheDocument();
      expect(screen.getByText('Treba mi pristup')).toBeInTheDocument();
      expect(screen.getByText('Gotov zahtjev')).toBeInTheDocument();
      await user.click(tab(/^Inbox$/));
      expect(screen.getByText('Printer ne radi')).toBeInTheDocument();
    });

    it('says what is missing when a tab has nothing in it', async () => {
      const user = userEvent.setup();
      renderPage({ ...newEmailState(), messages: [message('m1', 'Printer ne radi')] });
      await screen.findByText('Printer ne radi');

      await user.click(tab(/^Marked for tickets/));
      expect(screen.getByText('No emails marked as tickets yet.')).toBeInTheDocument();
    });

    it('says so when there are no messages at all', async () => {
      renderPage({ ...newEmailState(), messages: [] });

      expect(await screen.findByText('No emails found.')).toBeInTheDocument();
    });

    it('asks for the messages again, and shows what is new, when Refresh is pressed', async () => {
      const user = userEvent.setup();
      const state = newEmailState();
      renderPage(state);
      await screen.findByText('Printer ne radi');
      state.messages.unshift(message('m9', 'Nova poruka'));

      await user.click(screen.getByRole('button', { name: 'Refresh' }));

      expect(await screen.findByText('Nova poruka')).toBeInTheDocument();
      expect(inboxRequests()).toHaveLength(2);
    });

    it('says that the inbox could not be read, and what to try, when it cannot', async () => {
      renderPage(newEmailState(), (s) => s.refuse('GET', /\/messages$/, 500, {}));

      expect(await screen.findByText('Could not load your inbox. Try reconnecting your mailbox.')).toBeInTheDocument();
      expect(screen.queryByText('No emails found.')).not.toBeInTheDocument();
      expect(screen.queryByText('Loading emails…')).not.toBeInTheDocument();
    });
  });

  describe('reading a message', () => {
    it('shows the message that is chosen, from the list by its subject or by the row, with who it is from and when', async () => {
      const user = userEvent.setup();
      const state = newEmailState();
      renderPage(state);
      await screen.findByText('Printer ne radi');
      expect(screen.getByText('Select an email to read it.')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /Printer ne radi/ }));
      expect(await screen.findByText('Tekst poruke Printer ne radi')).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Printer ne radi' })).toBeInTheDocument();
      expect(screen.getByText(new RegExp(`From Mia Marić <mia@klijent.test> · ${format(new Date(state.messages[0].receivedAtUtc), 'dd.MM.yyyy HH:mm')}`))).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Printer ne radi/ })).toHaveAttribute('aria-current', 'true');

      await user.click(within(rowOf('Treba mi pristup')).getByText('m2@klijent.test'));
      expect(await screen.findByText('Tekst poruke Treba mi pristup')).toBeInTheDocument();
      expect(screen.getByText(/From m2@klijent.test ·/)).toBeInTheDocument();
      expect(server.sent('GET', /prefill$/).map((r) => r.url)).toEqual([
        '/api/email-integration/messages/m1/prefill', '/api/email-integration/messages/m2/prefill',
      ]);
    });

    it('asks for the message by its id with every special character in it written out, since ids of mailboxes have slashes, pluses and equals signs', async () => {
      const user = userEvent.setup();
      renderPage({ ...newEmailState(), messages: [message('AAMk/x+y==', 'Čudan id')] });

      await user.click(await screen.findByRole('button', { name: /Čudan id/ }));

      expect(await screen.findByText('Tekst poruke Čudan id')).toBeInTheDocument();
      expect(server.sent('GET', /prefill$/)[0].url).toBe('/api/email-integration/messages/AAMk%2Fx%2By%3D%3D/prefill');
    });

    it('offers to open a ticket from a message that is marked, and to see the ticket when it has been made, and neither for one that is not marked', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');

      await user.click(screen.getByRole('button', { name: /Printer ne radi/ }));
      await screen.findByText('Tekst poruke Printer ne radi');
      expect(screen.queryByRole('link', { name: /ticket →/ })).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /Treba mi pristup/ }));
      expect(await screen.findByRole('link', { name: 'Open ticket →' })).toHaveAttribute('href', '/tickets/new-on-behalf?fromEmail=m2');

      await user.click(screen.getByRole('button', { name: /Gotov zahtjev/ }));
      expect(await screen.findByRole('link', { name: 'View ticket →' })).toHaveAttribute('href', '/tickets/t5');
    });

    it('says that the message could not be loaded', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      server.refuse('GET', /m1\/prefill$/, 500, {});

      await user.click(screen.getByRole('button', { name: /Printer ne radi/ }));

      expect(await screen.findByText('Could not load this email.')).toBeInTheDocument();
    });
  });

  describe('marking a message for a ticket', () => {
    it('marks it with the checkbox, without choosing the message, and the count in the tab goes up', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');

      await user.click(within(rowOf('Printer ne radi')).getByRole('checkbox'));

      await waitFor(() => expect(tab(/^Marked for tickets/)).toHaveTextContent('Marked for tickets (3)'));
      expect(within(rowOf('Printer ne radi')).getByRole('checkbox')).toBeChecked();
      expect(server.sent('POST', /m1\/mark$/).map((r) => r.body)).toEqual([{ marked: true }]);
      expect(screen.getByText('Select an email to read it.')).toBeInTheDocument();
    });

    it('takes the mark off again, and the count goes down', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Treba mi pristup');

      await user.click(within(rowOf('Treba mi pristup')).getByRole('checkbox'));

      await waitFor(() => expect(tab(/^Marked for tickets/)).toHaveTextContent('Marked for tickets (1)'));
      expect(server.sent('POST', /m2\/mark$/).map((r) => r.body)).toEqual([{ marked: false }]);
    });
  });

  describe('disconnecting the mailbox', () => {
    it('asks first, says who loses the inbox, and does nothing when the question is answered with Cancel', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');

      await user.click(screen.getByRole('button', { name: 'Disconnect' }));
      const dialog = await screen.findByRole('dialog', { name: 'Disconnect the branch mailbox?' });
      expect(within(dialog).getByText('Everyone in your branch will lose access to this inbox until someone reconnects it.')).toBeInTheDocument();
      await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(server.sent('POST', /disconnect$/)).toHaveLength(0);
      expect(screen.getByText('Printer ne radi')).toBeInTheDocument();
    });

    it('disconnects when the question is answered with Disconnect, and offers to connect again', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      await user.click(screen.getByRole('button', { name: 'Disconnect' }));

      await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Disconnect' }));

      expect(await connectButton()).toBeInTheDocument();
      expect(server.sent('POST', /disconnect$/)).toHaveLength(1);
      expect(screen.queryByText('Printer ne radi')).not.toBeInTheDocument();
    });
  });

  describe('what the page says while it waits, and when it is told not to wait', () => {
    it('says that it is checking the mailbox, with the spinner named, until it knows', async () => {
      const held = { release: () => {} };
      renderPage(newEmailState(), (s) => Object.assign(held, s.hold('GET', /email-integration\/status$/)));

      expect(await screen.findByRole('status', { name: 'Loading the mailbox' })).toBeInTheDocument();
      held.release();
      await screen.findByText('Printer ne radi');
      expect(screen.queryByRole('status', { name: 'Loading the mailbox' })).not.toBeInTheDocument();
    });

    it('names the tabs', async () => {
      renderPage();
      await screen.findByText('Printer ne radi');

      expect(screen.getByRole('tablist', { name: 'Mailbox' })).toBeInTheDocument();
    });

    it('says that it is disconnecting, and cannot be pressed again, while the server is told', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      const held = server.hold('POST', /disconnect$/);
      await user.click(screen.getByRole('button', { name: 'Disconnect' }));

      await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Disconnect' }));

      expect(screen.getByRole('button', { name: 'Disconnecting…' })).toBeDisabled();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      held.release();
      await connectButton();
    });

    it('does not keep the first reason that connecting failed while it is tried again', async () => {
      const user = userEvent.setup();
      renderPage({ ...newEmailState(), connected: false, connectedEmail: null }, (s) => s.refuse('GET', /email-integration\/config$/, 500, {}));
      await user.click(await connectButton());
      await screen.findByText(/could not be started/);
      const held = server.hold('GET', /email-integration\/config$/);

      await user.click(await connectButton());

      expect(screen.queryByText(/could not be started/)).not.toBeInTheDocument();
      held.release();
      await waitFor(() => expect(redirectTo).toHaveBeenCalled());
    });

    it('hands the id of the message to the new ticket with the special characters of the id written out', async () => {
      const user = userEvent.setup();
      renderPage({ ...newEmailState(), messages: [message('AAMk/x+y==', 'Čudan id', { isMarked: true })] });

      await user.click(await screen.findByRole('button', { name: /Čudan id/ }));

      expect(await screen.findByRole('link', { name: 'Open ticket →' })).toHaveAttribute('href', '/tickets/new-on-behalf?fromEmail=AAMk%2Fx%2By%3D%3D');
    });

    it('does not keep a message open that is not among those of the tab that was chosen', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      await user.click(screen.getByRole('button', { name: /Printer ne radi/ }));
      await screen.findByText('Tekst poruke Printer ne radi');

      await user.click(tab(/^Marked for tickets/));

      expect(screen.getByText('Select an email to read it.')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Printer ne radi' })).not.toBeInTheDocument();
    });
  });

  describe('what it used to get wrong', () => {
    it('says so, and offers to try again, when it cannot be told whether the mailbox is connected, instead of offering to connect one', async () => {
      const user = userEvent.setup();
      renderPage(newEmailState(), (s) => s.refuse('GET', /email-integration\/status$/, 500, {}));

      expect(await screen.findByText('Could not check whether the mailbox is connected. Try again in a moment.')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /Connect branch mailbox/ })).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Try again' }));
      expect(await screen.findByText('Printer ne radi')).toBeInTheDocument();
    });

    it('does not ask the server about a mailbox for somebody who is in no branch', async () => {
      useAuthStore.setState({ accessToken: 't', user: { ...employee, departmentId: null } as UserSummary });
      renderPage();

      await screen.findByText(/You're not assigned to a branch yet/);
      expect(server.sent('GET', /email-integration\/status$/)).toHaveLength(0);
    });

    it('says that the messages are on their way, and not that there are none, until they have come', async () => {
      const held = { release: () => {} };
      renderPage(newEmailState(), (s) => Object.assign(held, s.hold('GET', /\/messages$/)));
      await screen.findByText('Connected: sarajevo@firma.test');

      expect(screen.getByText('Loading emails…')).toBeInTheDocument();
      expect(screen.queryByText('No emails found.')).not.toBeInTheDocument();
      held.release();
      await screen.findByText('Printer ne radi');
      expect(screen.queryByText('Loading emails…')).not.toBeInTheDocument();
    });

    it('does not keep saying that the inbox could not be read once it has been read', async () => {
      const user = userEvent.setup();
      renderPage(newEmailState(), (s) => s.refuse('GET', /\/messages$/, 500, {}));
      await screen.findByText('Could not load your inbox. Try reconnecting your mailbox.');

      await user.click(screen.getByRole('button', { name: 'Refresh' }));

      expect(await screen.findByText('Printer ne radi')).toBeInTheDocument();
      expect(screen.queryByText('Could not load your inbox. Try reconnecting your mailbox.')).not.toBeInTheDocument();
    });

    it('says that it is refreshing, and cannot be pressed again, while it is', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      const held = server.hold('GET', /\/messages$/);

      await user.click(screen.getByRole('button', { name: 'Refresh' }));

      expect(screen.getByRole('button', { name: 'Refreshing…' })).toBeDisabled();
      held.release();
      await screen.findByRole('button', { name: 'Refresh' });
      expect(inboxRequests()).toHaveLength(2);
    });

    it('says why, and shows no spinner for ever, when the message cannot be loaded', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      server.refuse('GET', /m1\/prefill$/, 500, {});

      await user.click(screen.getByRole('button', { name: /Printer ne radi/ }));

      expect(await screen.findByText('Could not load this email.')).toBeInTheDocument();
      expect(screen.queryByRole('status', { name: 'Loading the email' })).not.toBeInTheDocument();
      expect(document.querySelector('.animate-spin')).toBeNull();
    });

    it('shows the message that was chosen last, with its own sender, even when the answer for the one before arrives last', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      const early = server.hold('GET', /m1\/prefill$/);

      await user.click(screen.getByRole('button', { name: /Printer ne radi/ }));
      await user.click(screen.getByRole('button', { name: /Treba mi pristup/ }));
      await screen.findByText('Tekst poruke Treba mi pristup');
      early.release();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });

      expect(screen.getByRole('heading', { name: 'Treba mi pristup' })).toBeInTheDocument();
      expect(screen.queryByText('Tekst poruke Printer ne radi')).not.toBeInTheDocument();
      expect(screen.getByText(/From m2@klijent.test/)).toBeInTheDocument();
    });

    it('says that the message is on its way, with the spinner named', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      const held = server.hold('GET', /m1\/prefill$/);

      await user.click(screen.getByRole('button', { name: /Printer ne radi/ }));

      expect(screen.getByRole('status', { name: 'Loading the email' })).toBeInTheDocument();
      held.release();
      await screen.findByText('Tekst poruke Printer ne radi');
    });

    it('says that the mark could not be changed, and leaves the checkbox as it was, when the server refuses', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      server.refuse('POST', /m1\/mark$/, 500, {});

      await user.click(within(rowOf('Printer ne radi')).getByRole('checkbox'));

      expect(await screen.findByText('Could not change the mark. Try again.')).toBeInTheDocument();
      expect(within(rowOf('Printer ne radi')).getByRole('checkbox')).not.toBeChecked();
      expect(tab(/^Marked for tickets/)).toHaveTextContent('Marked for tickets (2)');
    });

    it('takes back the message about the mark when it is tried again and works', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      server.refuse('POST', /m1\/mark$/, 500, {});
      await user.click(within(rowOf('Printer ne radi')).getByRole('checkbox'));
      await screen.findByText('Could not change the mark. Try again.');

      await user.click(within(rowOf('Printer ne radi')).getByRole('checkbox'));

      await waitFor(() => expect(within(rowOf('Printer ne radi')).getByRole('checkbox')).toBeChecked());
      expect(screen.queryByText('Could not change the mark. Try again.')).not.toBeInTheDocument();
    });

    it('says that the mailbox could not be disconnected, stays connected, and can be tried again', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');
      server.refuse('POST', /disconnect$/, 500, {});
      await user.click(screen.getByRole('button', { name: 'Disconnect' }));

      await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Disconnect' }));

      expect(await screen.findByText('Could not disconnect the mailbox. Try again.')).toBeInTheDocument();
      expect(screen.getByText('Printer ne radi')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Disconnect' })).toBeEnabled();
    });

    it('names each checkbox after the message it is for', async () => {
      renderPage();
      await screen.findByText('Printer ne radi');

      expect(screen.getByRole('checkbox', { name: 'Mark "Printer ne radi" as a ticket' })).toBeInTheDocument();
      expect(screen.getByRole('checkbox', { name: 'Mark "Treba mi pristup" as a ticket' })).toBeInTheDocument();
    });

    it('has the inbox and the marked messages as tabs, with the one that is chosen said to be chosen', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Printer ne radi');

      expect(screen.getAllByRole('tab').map((t) => [t.textContent, t.getAttribute('aria-selected')])).toEqual([['Inbox', 'true'], ['Marked for tickets (2)', 'false']]);

      await user.click(screen.getByRole('tab', { name: /^Marked/ }));
      expect(screen.getByRole('tab', { name: /^Marked/ })).toHaveAttribute('aria-selected', 'true');
    });

    it('says how many messages are listed when it is the most that is asked for, so that it is not taken for all of them', async () => {
      const many = Array.from({ length: 40 }, (_, i) => message(`n${i}`, `Poruka ${i}`));
      renderPage({ ...newEmailState(), messages: many });

      expect(await screen.findByText('Showing the latest 30 emails.')).toBeInTheDocument();
    });

    it('does not say that when all the messages are listed', async () => {
      renderPage();
      await screen.findByText('Printer ne radi');

      expect(screen.queryByText(/Showing the latest/)).not.toBeInTheDocument();
    });
  });
});
