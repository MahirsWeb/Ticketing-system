import { installFakeHttp, refuse } from './fakeHttp';

/**
 * A stand-in for the part of the server that the mailbox pages talk to, kept in memory and answering at the level of HTTP. It has the rules
 * the pages have to live with: the inbox can only be read while a mailbox is connected, the messages are the latest ones up to what was asked
 * for, and the mark on a message is the server's (the answer to marking is what the mark is now).
 *
 * The test file must mock `../api/client` for this to take effect (`get` and `post`).
 */

export interface FakeMessage {
  messageId: string;
  subject: string;
  fromEmail: string;
  fromName: string;
  bodyPreview: string;
  receivedAtUtc: string;
  hasAttachments: boolean;
  isMarked: boolean;
  convertedTicketId: string | null;
  bodyHtml: string;
}

export interface EmailState {
  connected: boolean;
  connectedEmail: string | null;
  clientId: string;
  messages: FakeMessage[];
}

const hoursAgo = (hours: number) => new Date(Date.now() - hours * 60 * 60 * 1000).toISOString();

export const message = (id: string, subject: string, extra: Partial<FakeMessage> = {}): FakeMessage => ({
  messageId: id, subject, fromEmail: `${id}@klijent.test`, fromName: '', bodyPreview: `Pregled ${subject}`, receivedAtUtc: hoursAgo(2),
  hasAttachments: false, isMarked: false, convertedTicketId: null, bodyHtml: `<p>Tekst poruke ${subject}</p>`, ...extra,
});

export function newEmailState(): EmailState {
  return {
    connected: true,
    connectedEmail: 'sarajevo@firma.test',
    clientId: 'client-id',
    messages: [
      message('m1', 'Printer ne radi', { fromName: 'Mia Marić', fromEmail: 'mia@klijent.test', hasAttachments: true }),
      message('m2', 'Treba mi pristup', { isMarked: true }),
      message('m3', 'Gotov zahtjev', { fromName: 'Nedim Nuhić', isMarked: true, convertedTicketId: 't5' }),
    ],
  };
}

export function installEmailBackend(state: EmailState = newEmailState()) {
  function handle(method: string, url: string, body: any, params: any): unknown {
    if (method === 'GET' && url === '/api/email-integration/status') return { connected: state.connected, connectedEmail: state.connectedEmail };
    if (method === 'GET' && url === '/api/email-integration/config') return { clientId: state.clientId };
    if (method === 'POST' && url === '/api/email-integration/connect') {
      state.connected = true;
      state.connectedEmail = 'sarajevo@firma.test';
      return { connected: true, connectedEmail: state.connectedEmail };
    }
    if (method === 'POST' && url === '/api/email-integration/disconnect') {
      state.connected = false;
      state.connectedEmail = null;
      return null;
    }
    if (method === 'GET' && url === '/api/email-integration/messages') {
      if (!state.connected) refuse(400, { message: 'No mailbox is connected.' });
      return state.messages.slice(0, params?.take ?? 25).map(({ bodyHtml: _body, ...summary }) => summary);
    }
    const one = url.match(/^\/api\/email-integration\/messages\/([^/]+)\/(mark|prefill|complete-ticket)$/);
    if (one) {
      const found = state.messages.find((m) => m.messageId === decodeURIComponent(one[1])) ?? refuse(404, { message: 'That message is not in the mailbox.' });
      if (method === 'POST' && one[2] === 'mark') {
        found.isMarked = !!body.marked;
        return found.isMarked;
      }
      if (method === 'GET' && one[2] === 'prefill') {
        return { messageId: found.messageId, clientEmail: found.fromEmail, subject: found.subject, bodyHtml: found.bodyHtml, receivedAtUtc: found.receivedAtUtc, hasAttachments: found.hasAttachments };
      }
      if (method === 'POST' && one[2] === 'complete-ticket') {
        found.convertedTicketId = 'ticket-' + found.messageId;
        return null;
      }
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  }

  return { state, ...installFakeHttp(handle) };
}
