import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithQuery } from '../test/renderWithQuery';
import { INBOX_SIZE, useEmailPrefill, useEmailStatus, useInbox } from './emailQueries';
import { emailKeys } from './emailKeys';
import { emailIntegrationApi, type EmailConnectionStatusDto, type InboxMessageSummaryDto } from './emailIntegration';
import { queryClient } from './queryClient';
import { apiClient } from './client';

vi.mock('./client', () => ({ apiClient: { get: vi.fn(), post: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const asked = (url: string) => get.mock.calls.filter((c) => c[0] === url).length;

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const summary = (id: string, marked = false): InboxMessageSummaryDto => ({
  messageId: id, subject: `Tema ${id}`, fromEmail: `${id}@x.test`, fromName: '', bodyPreview: '', receivedAtUtc: '2026-06-10T09:00:00Z',
  hasAttachments: false, isMarked: marked, convertedTicketId: null,
});

function Status({ enabled = true }: { enabled?: boolean }) {
  const q = useEmailStatus({ enabled });
  return <p data-testid="status">{q.isError ? 'failed' : q.data ? (q.data.connected ? `connected ${q.data.connectedEmail}` : 'not connected') : 'loading'}</p>;
}
function Inbox() {
  const q = useInbox();
  return <p data-testid="inbox">{q.data ? q.data.map((m) => `${m.messageId}:${m.isMarked ? 'marked' : 'plain'}`).join(',') : 'loading'}</p>;
}
function Prefill({ id }: { id: string | null }) {
  const q = useEmailPrefill(id);
  return <p data-testid="prefill">{q.isError ? 'failed' : q.data ? q.data.subject : 'loading'}</p>;
}
function Switch() {
  const [id, setId] = useState('m1');
  return (
    <>
      <button onClick={() => setId('m2')}>open the other message</button>
      <Prefill id={id} />
    </>
  );
}

describe('the mailbox data the screens share (T-17)', () => {
  beforeEach(() => {
    queryClient.clear();
    get.mockReset();
    post.mockReset();
    get.mockImplementation((async (url: string) => {
      if (url === '/api/email-integration/status') return { data: { connected: true, connectedEmail: 'a@firma.test' } };
      if (url === '/api/email-integration/messages') return { data: [summary('m1'), summary('m2')] };
      return { data: { messageId: 'x', clientEmail: 'x@x.test', subject: `Tema ${url.split('/').at(-2)}`, bodyHtml: '', receivedAtUtc: '', hasAttachments: false } };
    }) as never);
  });
  afterEach(() => queryClient.clear());

  it('asks for the latest messages, the number that is agreed, and once however many places show them', async () => {
    renderWithQuery(
      <>
        <Inbox />
        <Inbox />
      </>,
      queryClient
    );

    await waitFor(() => expect(screen.getAllByText('m1:plain,m2:plain')).toHaveLength(2));
    expect(asked('/api/email-integration/messages')).toBe(1);
    expect(get.mock.calls.find((c) => c[0] === '/api/email-integration/messages')![1]).toEqual({ params: { take: INBOX_SIZE } });
    expect(INBOX_SIZE).toBe(30);
  });

  it('forgets the status, the inbox and a message when they are left', async () => {
    const view = renderWithQuery(
      <>
        <Status />
        <Inbox />
        <Prefill id="m1" />
      </>,
      queryClient
    );
    await screen.findByText('connected a@firma.test');
    await screen.findByText('m1:plain,m2:plain');
    await screen.findByText('Tema m1');

    view.unmount();

    await waitFor(() => expect(queryClient.getQueryData(emailKeys.status)).toBeUndefined());
    expect(queryClient.getQueryData(emailKeys.inbox)).toBeUndefined();
    expect(queryClient.getQueryData(emailKeys.prefill('m1'))).toBeUndefined();
  });

  it('asks nothing about the status where it is of no use, and nothing about a message while none is chosen', () => {
    renderWithQuery(
      <>
        <Status enabled={false} />
        <Prefill id={null} />
      </>,
      queryClient
    );

    expect(get).not.toHaveBeenCalled();
  });

  it('keeps each message as an entry of its own, so a slow answer for the one that was chosen before is never shown for the one chosen now', async () => {
    const user = userEvent.setup();
    const slow = deferred<{ data: unknown }>();
    get.mockImplementation((async (url: string) =>
      url.includes('/m1/') ? slow.promise : { data: { subject: 'Tema m2', bodyHtml: '', clientEmail: '', receivedAtUtc: '', hasAttachments: false, messageId: 'm2' } }) as never);
    renderWithQuery(<Switch />, queryClient);

    await user.click(screen.getByRole('button', { name: 'open the other message' }));
    await screen.findByText('Tema m2');
    await act(async () => slow.resolve({ data: { subject: 'Tema m1', bodyHtml: '', clientEmail: '', receivedAtUtc: '', hasAttachments: false, messageId: 'm1' } }));

    expect(screen.getByTestId('prefill')).toHaveTextContent('Tema m2');
  });

  describe('marking a message', () => {
    it('takes what the server says the mark is into the message that is on screen at once, and not into the other messages', async () => {
      renderWithQuery(<Inbox />, queryClient);
      await screen.findByText('m1:plain,m2:plain');
      post.mockResolvedValue({ data: true } as never);

      await act(async () => {
        await emailIntegrationApi.setMarked('m2', true);
      });

      await waitFor(() => expect(screen.getByTestId('inbox')).toHaveTextContent('m1:plain,m2:marked'));
      expect(post.mock.calls).toEqual([['/api/email-integration/messages/m2/mark', { marked: true }]]);
    });

    it('takes what the server says, not what it was asked: a mark that the server did not set is not shown as set', async () => {
      renderWithQuery(<Inbox />, queryClient);
      await screen.findByText('m1:plain,m2:plain');
      post.mockResolvedValue({ data: false } as never);

      await act(async () => {
        await emailIntegrationApi.setMarked('m1', true);
      });

      const rows = queryClient.getQueryData<InboxMessageSummaryDto[]>(emailKeys.inbox)!;
      expect(rows.map((m) => [m.messageId, m.isMarked])).toEqual([['m1', false], ['m2', false]]);
    });

    it('takes a mark that the server did set, even when what was asked was to take it off', async () => {
      renderWithQuery(<Inbox />, queryClient);
      await screen.findByText('m1:plain,m2:plain');
      post.mockResolvedValue({ data: true } as never);

      await act(async () => {
        await emailIntegrationApi.setMarked('m1', false);
      });

      const rows = queryClient.getQueryData<InboxMessageSummaryDto[]>(emailKeys.inbox)!;
      expect(rows.map((m) => [m.messageId, m.isMarked])).toEqual([['m1', true], ['m2', false]]);
    });

    it('writes the id of the message into its address with the special characters written out', async () => {
      post.mockResolvedValue({ data: true } as never);

      await emailIntegrationApi.setMarked('AAMk/x+y==', true);

      expect(post.mock.calls[0][0]).toBe('/api/email-integration/messages/AAMk%2Fx%2By%3D%3D/mark');
    });

    it('puts nothing into memory when the inbox is not on screen, and changes nothing when the server refuses', async () => {
      post.mockResolvedValue({ data: true } as never);
      await emailIntegrationApi.setMarked('m1', true);
      expect(queryClient.getQueryData(emailKeys.inbox)).toBeUndefined();

      renderWithQuery(<Inbox />, queryClient);
      await screen.findByText('m1:plain,m2:plain');
      post.mockRejectedValue(new Error('refused'));
      await act(async () => {
        await emailIntegrationApi.setMarked('m1', true).catch(() => undefined);
      });

      expect(screen.getByTestId('inbox')).toHaveTextContent('m1:plain,m2:plain');
    });
  });

  describe('disconnecting the mailbox, and connecting it', () => {
    it('shows the status that is on screen as not connected at once, and asks for it again to be sure', async () => {
      renderWithQuery(<Status />, queryClient);
      await screen.findByText('connected a@firma.test');
      const second = deferred<{ data: EmailConnectionStatusDto }>();
      get.mockImplementation((() => second.promise) as never);
      post.mockResolvedValue({ data: null } as never);

      await act(async () => {
        await emailIntegrationApi.disconnect();
      });

      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('not connected'));
      expect(asked('/api/email-integration/status')).toBe(2);
      await act(async () => second.resolve({ data: { connected: false, connectedEmail: null } }));
    });

    it('puts nothing into memory for a status that is not on screen', async () => {
      post.mockResolvedValue({ data: null } as never);

      await emailIntegrationApi.disconnect();

      expect(queryClient.getQueryData(emailKeys.status)).toBeUndefined();
    });

    it('does not touch what is shown when the server refuses to disconnect', async () => {
      renderWithQuery(<Status />, queryClient);
      await screen.findByText('connected a@firma.test');
      post.mockRejectedValue(new Error('refused'));

      await act(async () => {
        await emailIntegrationApi.disconnect().catch(() => undefined);
      });

      expect(screen.getByTestId('status')).toHaveTextContent('connected a@firma.test');
      expect(asked('/api/email-integration/status')).toBe(1);
    });

    it('asks for the status again when a mailbox is connected, and sends what it was given', async () => {
      renderWithQuery(<Status />, queryClient);
      await screen.findByText('connected a@firma.test');
      post.mockResolvedValue({ data: { connected: true, connectedEmail: 'b@firma.test' } } as never);

      await act(async () => {
        await emailIntegrationApi.connect('the-code', 'https://app/emails/callback', 'the-verifier');
      });

      expect(post.mock.calls).toEqual([['/api/email-integration/connect', { code: 'the-code', redirectUri: 'https://app/emails/callback', codeVerifier: 'the-verifier' }]]);
      await waitFor(() => expect(asked('/api/email-integration/status')).toBe(2));
    });
  });
});
