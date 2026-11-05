import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AppLayout from './AppLayout';
import { ticketsApi } from '../api/tickets';
import { LanguageProvider } from '../i18n/LanguageContext';
import { useAuthStore } from '../store/authStore';
import { renderWithQuery } from '../test/renderWithQuery';
import type { UserSummary } from '../types';

vi.mock('../api/tickets', () => ({
  ticketsApi: { counts: vi.fn().mockResolvedValue({ total: 4, mine: 1, opened: 2, closed: 1, new: 1 }) },
}));
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
