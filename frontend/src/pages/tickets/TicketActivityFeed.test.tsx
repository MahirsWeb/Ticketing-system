import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TicketActivityFeed } from './TicketActivityFeed';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto } from '../../types';

vi.mock('../../api/tickets', () => ({ ticketsApi: { addMessage: vi.fn(), uploadAttachments: vi.fn() } }));
// The real editor (TipTap) needs a browser; what matters here is what the feed does with the text.
vi.mock('../../components/RichTextEditor', () => ({
  RichTextEditor: ({ value, onChange, onFilesSelected }: { value: string; onChange: (v: string) => void; onFilesSelected?: (f: File[]) => void }) => (
    <>
      <textarea aria-label="editor" value={value} onChange={(e) => onChange(e.target.value)} />
      <button type="button" onClick={() => onFilesSelected?.([new File(['x'], 'screenshot.png')])}>
        pick a file
      </button>
    </>
  ),
}));

const addMessage = vi.mocked(ticketsApi.addMessage);
const ticket = { id: 'ticket-1' } as TicketDetailDto;

function renderFeed(options: { response?: boolean; internal?: boolean; onMessageAdded?: () => void } = {}) {
  return render(
    <TicketActivityFeed
      ticket={ticket}
      messages={[]}
      canReplyResponse={options.response ?? true}
      canReplyInternal={options.internal ?? false}
      onMessageAdded={options.onMessageAdded ?? (() => {})}
    />
  );
}

const sent = (n: number) => ({ id: `m-${n}`, attachments: [] });

describe('the reply box of a ticket', () => {
  beforeEach(() => {
    addMessage.mockReset();
  });

  it('sends the message with a key that identifies it, and empties the box when it went through', async () => {
    addMessage.mockResolvedValueOnce(sent(1));
    const added = vi.fn();
    renderFeed({ onMessageAdded: added });

    await userEvent.type(screen.getByLabelText('editor'), 'Hello');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(addMessage).toHaveBeenCalledTimes(1);
    const [id, type, body, key] = addMessage.mock.calls[0];
    expect([id, type, body]).toEqual(['ticket-1', 'Response', 'Hello']);
    expect(key).toMatch(/^[A-Za-z0-9_-]{8,64}$/);
    expect(added).toHaveBeenCalledWith(expect.objectContaining({ id: 'm-1' }));
    expect(screen.getByLabelText('editor')).toHaveValue('');
  });

  it('sends nothing for an empty box', async () => {
    renderFeed();

    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(addMessage).not.toHaveBeenCalled();
  });

  // NOVO-FE-05: a reply that only looks empty (a space, a no-break space) used to be sent to the client.
  it.each(['<p> </p>', '<p>&nbsp;</p>', '<p><br></p>'])('sends nothing for %s, which only looks empty', async (html) => {
    renderFeed();

    // userEvent.type treats "<" as the start of a key description, so the text goes in with paste.
    await userEvent.click(screen.getByLabelText('editor'));
    await userEvent.paste(html);
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(addMessage).not.toHaveBeenCalled();
  });

  it('keeps the text and says why when the sending failed, and the next try is the SAME message (same key)', async () => {
    addMessage.mockRejectedValueOnce({ response: { data: { message: 'The server is busy.' } } }).mockResolvedValueOnce(sent(1));
    renderFeed();

    await userEvent.type(screen.getByLabelText('editor'), 'Hello');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(screen.getByRole('alert')).toHaveTextContent('The server is busy.');
    expect(screen.getByLabelText('editor')).toHaveValue('Hello');

    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(addMessage).toHaveBeenCalledTimes(2);
    expect(addMessage.mock.calls[1][3]).toBe(addMessage.mock.calls[0][3]); // so the server can recognise it
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('treats a text that was edited after a failure as a different message (a different key)', async () => {
    addMessage.mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(sent(1));
    renderFeed();

    await userEvent.type(screen.getByLabelText('editor'), 'Hello');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));
    await userEvent.type(screen.getByLabelText('editor'), ' again');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(addMessage.mock.calls[1][3]).not.toBe(addMessage.mock.calls[0][3]);
  });

  it('gives each message its own key', async () => {
    addMessage.mockResolvedValue(sent(1));
    renderFeed();

    await userEvent.type(screen.getByLabelText('editor'), 'First');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));
    await userEvent.type(screen.getByLabelText('editor'), 'Second');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(addMessage.mock.calls[1][3]).not.toBe(addMessage.mock.calls[0][3]);
  });

  it('keeps what is being written when the page is left, e.g. because the sign-in expired, and gives it back', async () => {
    const first = renderFeed();
    await userEvent.type(screen.getByLabelText('editor'), 'A long reply I do not want to lose');
    first.unmount(); // the session expired: the person is taken to the sign-in page

    renderFeed();

    expect(screen.getByLabelText('editor')).toHaveValue('A long reply I do not want to lose');
  });

  it('forgets the draft once it was sent', async () => {
    addMessage.mockResolvedValueOnce(sent(1));
    const first = renderFeed();
    await userEvent.type(screen.getByLabelText('editor'), 'Sent soon');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));
    first.unmount();

    renderFeed();

    expect(screen.getByLabelText('editor')).toHaveValue('');
  });

  it('keeps a reply and an internal note apart', async () => {
    renderFeed({ response: true, internal: true });

    await userEvent.type(screen.getByLabelText('editor'), 'For the client');
    await userEvent.click(screen.getByRole('button', { name: 'Internal note' }));
    expect(screen.getByLabelText('editor')).toHaveValue('');
    await userEvent.type(screen.getByLabelText('editor'), 'For colleagues');

    await userEvent.click(screen.getByRole('button', { name: 'Response' }));
    expect(screen.getByLabelText('editor')).toHaveValue('For the client');
  });

  describe('attachments belong to the mode they were added in', () => {
    it('does not send a file added to an internal note along with the public reply', async () => {
      const uploadAttachments = vi.mocked(ticketsApi.uploadAttachments);
      uploadAttachments.mockReset();
      addMessage.mockResolvedValueOnce(sent(1));
      renderFeed({ response: true, internal: true });

      await userEvent.click(screen.getByRole('button', { name: 'Internal note' }));
      await userEvent.click(screen.getByRole('button', { name: 'pick a file' }));
      expect(screen.getByText(/screenshot\.png/)).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Response' }));
      expect(screen.queryByText(/screenshot\.png/)).not.toBeInTheDocument(); // not shown beside the public reply...
      await userEvent.type(screen.getByLabelText('editor'), 'Hello client');
      await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

      expect(addMessage).toHaveBeenCalledTimes(1);
      expect(uploadAttachments).not.toHaveBeenCalled(); // ...and not sent with it

      await userEvent.click(screen.getByRole('button', { name: 'Internal note' }));
      expect(screen.getByText(/screenshot\.png/)).toBeInTheDocument(); // still waiting with the note it was added to
    });

    it('gives the remove button a name that says which file it removes', async () => {
      renderFeed();
      await userEvent.click(screen.getByRole('button', { name: 'pick a file' }));

      await userEvent.click(screen.getByRole('button', { name: 'Remove screenshot.png' }));

      expect(screen.queryByText(/screenshot\.png/)).not.toBeInTheDocument();
    });
  });
});
