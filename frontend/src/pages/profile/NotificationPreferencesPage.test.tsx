import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import NotificationPreferencesPage from './NotificationPreferencesPage';
import { usersApi } from '../../api/users';
import { LanguageProvider } from '../../i18n/LanguageContext';

vi.mock('../../api/users', () => ({ usersApi: { getMyNotificationPreferences: vi.fn(), setMyNotificationPreferences: vi.fn() } }));

const staff = [
  { type: 'NewTicket', email: true },
  { type: 'NewResponse', email: true },
  { type: 'TaskAssigned', email: false },
];

function renderPage() {
  return render(
    <LanguageProvider>
      <NotificationPreferencesPage />
    </LanguageProvider>
  );
}

// TS-47.1: the person chooses which e-mails about tickets and tasks they get.
describe('the e-mail notification choices', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(usersApi.getMyNotificationPreferences).mockReset().mockResolvedValue(staff);
    vi.mocked(usersApi.setMyNotificationPreferences).mockReset().mockImplementation(async (p) => p);
  });

  it('shows the kinds the person is sent, each as it is now', async () => {
    renderPage();

    expect(await screen.findByRole('checkbox', { name: 'A new ticket arrives in my branch' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'There is a new reply on a ticket' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'A task is assigned to me' })).not.toBeChecked();
  });

  it('sends what was changed, and says it was saved', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole('checkbox', { name: 'There is a new reply on a ticket' }));

    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(usersApi.setMyNotificationPreferences).toHaveBeenCalledWith([
      { type: 'NewTicket', email: true },
      { type: 'NewResponse', email: false },
      { type: 'TaskAssigned', email: false },
    ]));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('says so, and keeps what the person ticked, when the settings could not be saved', async () => {
    const user = userEvent.setup();
    vi.mocked(usersApi.setMyNotificationPreferences).mockRejectedValueOnce(new Error('offline'));
    renderPage();
    await user.click(await screen.findByRole('checkbox', { name: 'A task is assigned to me' }));

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('The settings could not be saved.')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'A task is assigned to me' })).toBeChecked();
  });

  it('says so when the settings could not be loaded', async () => {
    vi.mocked(usersApi.getMyNotificationPreferences).mockRejectedValueOnce(new Error('offline'));
    renderPage();

    expect(await screen.findByText('The settings could not be loaded.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  });
});
