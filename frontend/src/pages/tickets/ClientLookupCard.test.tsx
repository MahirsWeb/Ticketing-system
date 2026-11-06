import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import userEvent from '@testing-library/user-event';
import { ClientLookupCard } from './ClientLookupCard';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installFakeHttp, refuse } from '../../test/fakeHttp';
import type { ClientLookupResult } from '../../types';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn() } }));

const LOOKUP = /^\/api\/users\/lookup-by-email$/;

const person = (first: string, email: string, extra: Partial<ClientLookupResult> = {}): ClientLookupResult =>
  ({ id: first.toLowerCase(), firstName: first, lastName: 'Klijentić', email, companyName: 'Acme', phoneNumber: '+38761000001', ...extra }) as ClientLookupResult;

const KNOWN: Record<string, ClientLookupResult> = {
  'sender@x.test': person('Sender', 'sender@x.test'),
  'typed@x.test': person('Typed', 'typed@x.test'),
  'other@x.test': person('Other', 'other@x.test'),
};

let server: ReturnType<typeof setup>;

/** The server as far as the card knows it: the clients that can be found by their e-mail address. */
function setup() {
  return installFakeHttp((method, url, _body, params) => {
    if (method === 'GET' && url === '/api/users/lookup-by-email') return KNOWN[params?.email as string] ?? refuse(404, { message: 'There is no client with that e-mail.' });
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  });
}

const found: (string | null)[] = [];
const onClient = (client: ClientLookupResult | null) => void found.push(client ? client.email : null);

function renderCard(props: { initialEmail?: string; autoLookupEmail?: string } = {}, before?: (s: ReturnType<typeof setup>) => void, client?: QueryClient) {
  found.length = 0;
  server = setup();
  before?.(server);
  const view = renderWithQuery(<ClientLookupCard client={null} onClient={onClient} {...props} />, client);
  return { user: userEvent.setup(), ...view };
}

const asked = () => server.sent('GET', LOOKUP);
const typeInto = async (user: ReturnType<typeof userEvent.setup>, email: string) => {
  const field = screen.getByPlaceholderText('client@company.com');
  await user.clear(field);
  await user.type(field, email);
  await user.click(screen.getByRole('button', { name: /Find client|Looking…/ }));
};
/** The page gives the card other properties: the same client of queries, as in the page. */
const showAgain = (view: ReturnType<typeof renderCard>, card: ReactElement) => view.rerender(<QueryClientProvider client={view.client}>{card}</QueryClientProvider>);
const settle = () => new Promise((resolve) => setTimeout(resolve, 40));

describe('finding the client', () => {
  it('names the field for the e-mail address (a placeholder is not a name)', () => {
    renderCard();

    expect(screen.getByRole('textbox', { name: 'Client email' })).toHaveAttribute('type', 'email');
  });

  describe('when the page knows who wrote', () => {
    it('asks for the sender once and tells who was found', async () => {
      renderCard({ initialEmail: 'sender@x.test', autoLookupEmail: 'sender@x.test' });

      await waitFor(() => expect(found).toEqual(['sender@x.test']));

      expect(asked()).toHaveLength(1);
      expect(asked()[0].params).toEqual({ email: 'sender@x.test' });
      expect(screen.getByPlaceholderText('client@company.com')).toHaveValue('sender@x.test');
    });

    it('asks for nobody when there is no sender', async () => {
      renderCard({ initialEmail: 'sender@x.test' });
      await settle();

      expect(asked()).toHaveLength(0);
      expect(found).toEqual([]);
    });

    it('says that the client has to be searched for by hand when no account was found for the sender', async () => {
      renderCard({ autoLookupEmail: 'nobody@x.test' });

      expect(await screen.findByRole('alert')).toHaveTextContent('Could not automatically find a client account for this email — search manually below.');
      expect(found).toEqual([]);
    });

    it('asks for the new address when the sender changes, and does not take an answer about the old one', async () => {
      let late!: { release: () => void };
      const view = renderCard({ autoLookupEmail: 'sender@x.test' }, (s) => { late = s.hold('GET', LOOKUP); });
      await waitFor(() => expect(asked()).toHaveLength(1));

      showAgain(view, <ClientLookupCard client={null} onClient={onClient} autoLookupEmail="other@x.test" />);
      await waitFor(() => expect(found).toEqual(['other@x.test']));
      late.release();
      await settle();

      expect(found).toEqual(['other@x.test']);
    });
  });

  describe('when the person searches by hand', () => {
    it('shows who was found and tells it', async () => {
      const { user } = renderCard();

      await typeInto(user, 'typed@x.test');

      await waitFor(() => expect(found).toEqual(['typed@x.test']));
      expect(asked()[0].params).toEqual({ email: 'typed@x.test' });
    });

    it('says that it is looking, and cannot be pressed again, until the answer is there', async () => {
      const { user } = renderCard({}, (s) => s.hold('GET', LOOKUP));

      await typeInto(user, 'typed@x.test');

      expect(screen.getByRole('button', { name: 'Looking…' })).toBeDisabled();
    });

    it('says what the server said when there is no client, and tells that nobody was found', async () => {
      const { user } = renderCard();

      await typeInto(user, 'nobody@x.test');

      expect(await screen.findByRole('alert')).toHaveTextContent('There is no client with that e-mail.');
      expect(found).toEqual([null]);
    });

    it('says so in its own words when the server said nothing', async () => {
      const { user } = renderCard({}, (s) => s.refuse('GET', LOOKUP, 500, {}, -1));

      await typeInto(user, 'typed@x.test');

      expect(await screen.findByRole('alert')).toHaveTextContent('No client found with that email.');
    });

    it('stops saying why the last search failed when the next one starts', async () => {
      const { user } = renderCard();
      await typeInto(user, 'nobody@x.test');
      await screen.findByRole('alert');
      server.hold('GET', LOOKUP);

      await typeInto(user, 'typed@x.test');

      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
  });

  describe('what was wrong', () => {
    it('does not ask again when the page gives it another function to tell with', async () => {
      const view = renderCard({ autoLookupEmail: 'sender@x.test' });
      await waitFor(() => expect(found).toEqual(['sender@x.test']));

      showAgain(view, <ClientLookupCard client={null} onClient={(client) => void found.push(client ? client.email : null)} autoLookupEmail="sender@x.test" />);
      showAgain(view, <ClientLookupCard client={null} onClient={(client) => void found.push(client ? client.email : null)} autoLookupEmail="sender@x.test" />);
      await settle();

      expect(asked()).toHaveLength(1);
      expect(found).toEqual(['sender@x.test']);
    });

    it('tells the one the page names when the answer comes, not the one it named when it asked', async () => {
      let late!: { release: () => void };
      const view = renderCard({ autoLookupEmail: 'sender@x.test' }, (s) => { late = s.hold('GET', LOOKUP); });
      await waitFor(() => expect(asked()).toHaveLength(1));
      const toldLater: (string | null)[] = [];

      showAgain(view, <ClientLookupCard client={null} onClient={(client) => void toldLater.push(client ? client.email : null)} autoLookupEmail="sender@x.test" />);
      late.release();
      await waitFor(() => expect(toldLater).toEqual(['sender@x.test']));

      expect(found).toEqual([]);
    });

    it('does not replace the client that was found by hand with the answer of the automatic search that comes after', async () => {
      let late!: { release: () => void };
      const { user } = renderCard({ autoLookupEmail: 'sender@x.test' }, (s) => { late = s.hold('GET', LOOKUP); });
      await waitFor(() => expect(asked()).toHaveLength(1));

      await typeInto(user, 'typed@x.test');
      await waitFor(() => expect(found).toEqual(['typed@x.test']));
      late.release();
      await settle();

      expect(found).toEqual(['typed@x.test']);
    });

    it('does not say that the automatic search failed when the client was found by hand in the meantime', async () => {
      let late!: { release: () => void };
      const { user } = renderCard({ autoLookupEmail: 'nobody@x.test' }, (s) => { late = s.hold('GET', LOOKUP); });
      await waitFor(() => expect(asked()).toHaveLength(1));

      await typeInto(user, 'typed@x.test');
      await waitFor(() => expect(found).toEqual(['typed@x.test']));
      late.release();
      await settle();

      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('does not keep who was found to show them from memory the next time (even where a client would keep what it fetched)', async () => {
      const view = renderCard({ autoLookupEmail: 'sender@x.test' }, undefined, new QueryClient({ defaultOptions: { queries: { retry: false } } }));
      await waitFor(() => expect(found).toEqual(['sender@x.test']));

      view.unmount();
      await settle();

      expect(view.client.getQueryCache().getAll()).toHaveLength(0);
    });

    it('stops looking for the sender when the card is left', async () => {
      const view = renderCard({ autoLookupEmail: 'sender@x.test' }, (s) => s.hold('GET', LOOKUP));
      await waitFor(() => expect(asked()).toHaveLength(1));
      const signal = asked()[0].signal;

      view.unmount();

      expect(signal?.aborted).toBe(true);
    });
  });
});
