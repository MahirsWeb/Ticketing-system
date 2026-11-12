import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AppLayout from './AppLayout';
import { ticketsApi } from '../api/tickets';
import { authApi } from '../api/auth';
import { usersApi } from '../api/users';
import { LanguageProvider } from '../i18n/LanguageContext';
import { useAuthStore } from '../store/authStore';
import { renderWithQuery } from '../test/renderWithQuery';
import type { UserSummary } from '../types';

vi.mock('../api/tickets', () => ({
  ticketsApi: { counts: vi.fn().mockResolvedValue({ total: 4, mine: 1, opened: 2, closed: 1, new: 1 }) },
}));
vi.mock('../api/auth', () => ({ authApi: { logout: vi.fn() } }));
vi.mock('../api/users', () => ({ usersApi: { setMyLanguage: vi.fn().mockResolvedValue({}), setMyPhone: vi.fn(), skipPhonePrompt: vi.fn() } }));
vi.mock('../api/notifications', () => ({ notificationsApi: { list: vi.fn().mockResolvedValue([]), markRead: vi.fn() } }));
vi.mock('../lib/notificationsHub', () => ({
  getNotificationsHub: () => ({ on: vi.fn(), off: vi.fn(), onreconnected: vi.fn() }),
}));

const employee = { id: 'u1', firstName: 'Amra', lastName: 'H', role: 'Employee', phoneNumberPrompted: true } as UserSummary;

function renderLayout() {
  return renderWithQuery(
    <LanguageProvider>
      <MemoryRouter initialEntries={['/tasks']}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/tasks" element={<p>the tasks</p>} />
            <Route path="/tickets" element={<p>the tickets</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </LanguageProvider>
  );
}

// TS-19.4: the tickets menu was a pointer-only hover panel made of clickable divs that stayed in the tab order while invisible.
describe('the tickets menu in the navigation', () => {
  beforeEach(() => useAuthStore.setState({ accessToken: 't', user: employee }));

  it('is a disclosure button that says whether it is open, and its items are real links', async () => {
    renderLayout();
    const toggle = await screen.findByRole('button', { name: /shortcuts/i });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const links = screen.getAllByRole('link').filter((a) => a.getAttribute('href')?.startsWith('/tickets'));
    expect(links.length).toBeGreaterThanOrEqual(4);
    expect(links.map((a) => a.getAttribute('href'))).toContain('/tickets?status=New');
    expect(document.querySelectorAll('div[role="link"]')).toHaveLength(0);
  });

  it('asks for the numbers again when the menu is opened or pointed at, so they are right when they are read', async () => {
    const counts = vi.mocked(ticketsApi.counts);
    counts.mockClear();
    renderLayout();
    const toggle = await screen.findByRole('button', { name: /shortcuts/i });
    await waitFor(() => expect(counts).toHaveBeenCalledTimes(1));

    await userEvent.hover(within(screen.getByRole('group', { name: /tickets/i })).getAllByRole('link')[0]);
    await waitFor(() => expect(counts).toHaveBeenCalledTimes(2));

    await userEvent.click(toggle);
    await waitFor(() => expect(counts).toHaveBeenCalledTimes(3));
  });

  it('closes on Escape and when something else gets the focus', async () => {
    renderLayout();
    const toggle = await screen.findByRole('button', { name: /shortcuts/i });

    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await userEvent.keyboard('{Escape}');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await act(async () => {
      screen.getByRole('button', { name: /sign out/i }).focus();
    });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
  });

  it('keeps its items out of the tab order while it is closed', async () => {
    renderLayout();
    const toggle = await screen.findByRole('button', { name: /shortcuts/i });
    const panel = document.getElementById(toggle.getAttribute('aria-controls')!)!;

    expect(panel.className).toContain('invisible');
    await userEvent.click(toggle);
    expect(panel.className).not.toMatch(/(^|\s)invisible(\s|$)/);
  });
});

// Signing out must wait for the server: until it confirms, the refresh cookie is still a live session for the next person at the computer.
describe('signing out', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 't', user: employee, signedOutOnPurpose: false });
    vi.mocked(authApi.logout).mockReset();
  });

  it('ends the session only after the server has confirmed it', async () => {
    let confirm!: () => void;
    vi.mocked(authApi.logout).mockReturnValue(new Promise<undefined>((resolve) => (confirm = () => resolve(undefined))) as never);
    renderLayout();

    await userEvent.click(await screen.findByRole('button', { name: /sign out/i }));
    expect(useAuthStore.getState().accessToken).toBe('t'); // still signed in while the answer is awaited

    await act(async () => confirm());
    await waitFor(() => expect(useAuthStore.getState().accessToken).toBeNull());
    expect(useAuthStore.getState().signedOutOnPurpose).toBe(true);
  });

  it('says so and stays signed in when the server could not be reached, instead of pretending', async () => {
    vi.mocked(authApi.logout).mockRejectedValue(new Error('Network Error'));
    renderLayout();

    await userEvent.click(await screen.findByRole('button', { name: /sign out/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/sign out failed/i);
    expect(useAuthStore.getState().accessToken).toBe('t');
    expect(screen.getByRole('button', { name: /sign out/i })).toBeEnabled(); // and they can try again
  });
});

// TS-47.1: e-mails are written on the server, in the language the person chose; the server is told, as the person signs in and when they change it.
describe('telling the server which language the person uses', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    vi.mocked(usersApi.setMyLanguage).mockClear();
    useAuthStore.setState({ accessToken: 't', user: employee });
  });

  it('reports the language on load and again when it is changed', async () => {
    renderLayout();
    await waitFor(() => expect(usersApi.setMyLanguage).toHaveBeenCalledWith('en'));

    await userEvent.click(screen.getAllByRole('button', { name: /bosanski|bs/i })[0]);

    await waitFor(() => expect(usersApi.setMyLanguage).toHaveBeenLastCalledWith('bs'));
  });

  it('does not tell the server again on a reload when the language has not changed', async () => {
    const first = renderLayout();
    await waitFor(() => expect(usersApi.setMyLanguage).toHaveBeenCalledTimes(1));
    first.unmount();

    renderLayout();
    expect(await screen.findByText('the tasks')).toBeInTheDocument();
    expect(usersApi.setMyLanguage).toHaveBeenCalledTimes(1);
  });

  it('is not bothered by the server being unable to take it', async () => {
    vi.mocked(usersApi.setMyLanguage).mockRejectedValueOnce(new Error('offline'));
    renderLayout();
    expect(await screen.findByText('the tasks')).toBeInTheDocument();
  });
});
