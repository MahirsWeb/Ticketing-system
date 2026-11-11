import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TicketActivityFeed } from './TicketActivityFeed';
import { ticketsApi } from '../../api/tickets';
import type { TicketDetailDto } from '../../types';

vi.mock('../../api/tickets', () => ({ ticketsApi: { addMessage: vi.fn(), uploadAttachments: vi.fn() } }));

// jsdom does no layout, and ProseMirror asks for rectangles when it scrolls the cursor into view after typing.
beforeAll(() => {
  const noRects = () => [] as unknown as DOMRectList;
  Element.prototype.getClientRects = noRects;
  Range.prototype.getClientRects = noRects;
  Range.prototype.getBoundingClientRect = () => new DOMRect();
});

const addMessage = vi.mocked(ticketsApi.addMessage);

/// TS-1.1 with the REAL editor (the other feed tests replace it with a plain text box, which cannot show what the editor displays):
/// a reply and an internal note are written in the same box, and what is shown must always be what will be sent.
describe('the reply box with the real editor', () => {
  beforeEach(() => {
    sessionStorage.clear();
    addMessage.mockReset();
  });

  it('shows the draft of the mode that is selected, and sends exactly that — never the internal note as a public reply', async () => {
    addMessage.mockResolvedValue({ id: 'm1', attachments: [] } as never);
    render(
      <TicketActivityFeed ticket={{ id: 'ticket-1' } as TicketDetailDto} messages={[]} canReplyResponse canReplyInternal onMessageAdded={() => {}} />
    );
    const box = () => screen.getByRole('textbox');

    box().focus();
    await userEvent.keyboard('A'); // a reply for the client
    await userEvent.click(screen.getByRole('button', { name: 'Internal note' }));
    box().focus();
    await userEvent.keyboard('B'); // a note for colleagues
    expect(box().textContent).toBe('B');

    await userEvent.click(screen.getByRole('button', { name: 'Response' }));
    expect(box().textContent).toBe('A'); // the reply draft is back on the screen, not the note
    expect(box().textContent).not.toContain('B');

    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));

    expect(addMessage).toHaveBeenCalledTimes(1);
    const [, type, body] = addMessage.mock.calls[0];
    expect(type).toBe('Response');
    expect(body).toContain('A');
    expect(body).not.toContain('B'); // the note's words never travel in the public reply
  });
});
