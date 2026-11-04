import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { NotificationBell } from './NotificationBell';
import { notificationsApi, type NotificationDto } from '../api/notifications';

vi.mock('../api/notifications', () => ({ notificationsApi: { list: vi.fn(), markRead: vi.fn() } }));
vi.mock('../lib/notificationsHub', () => ({
  getNotificationsHub: () => ({ on: vi.fn(), off: vi.fn(), onreconnected: vi.fn() }),
}));

const note = (id: string, isRead: boolean): NotificationDto => ({ id, type: 'NewTicket', message: `Message ${id}`, ticketId: null, isRead, createdAt: new Date().toISOString() });

function renderBell() {
  return render(
    <MemoryRouter>
      <NotificationBell />
    </MemoryRouter>
  );
}

describe('the notification bell', () => {
  beforeEach(() => vi.mocked(notificationsApi.list).mockReset());

  it('has a name that says how many notifications are unread (TS-46.5)', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([note('1', false), note('2', false), note('3', true)]);
    renderBell();

    expect(await screen.findByRole('button', { name: 'Notifications, 2 unread' })).toBeInTheDocument();
  });

  it('has a plain name when everything has been read, and does not read the emoji aloud', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([note('1', true)]);
    renderBell();

    const bell = await screen.findByRole('button', { name: 'Notifications' });
    expect(bell.querySelector('[aria-hidden]')).toHaveTextContent('🔔');
  });

  it('says whether the list is open', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([]);
    renderBell();
    const bell = await screen.findByRole('button', { name: 'Notifications' });

    expect(bell).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(bell);
    expect(bell).toHaveAttribute('aria-expanded', 'true');
  });
});
