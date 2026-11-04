import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Link } from 'react-router-dom';
import { DocumentMeta } from './DocumentMeta';
import { LanguageProvider, useLanguage } from '../i18n/LanguageContext';

function SwitchToBosnian() {
  const { setLanguage } = useLanguage();
  return <button onClick={() => setLanguage('bs')}>bs</button>;
}

function renderAt(path: string) {
  return render(
    <LanguageProvider>
      <MemoryRouter initialEntries={[path]}>
        <DocumentMeta />
        <Link to="/settings">go to settings</Link>
        <SwitchToBosnian />
      </MemoryRouter>
    </LanguageProvider>
  );
}

describe('DocumentMeta', () => {
  it('names the tab after the page the person is on', async () => {
    renderAt('/tickets');
    expect(document.title).toBe('Tickets · Ticket System Tech');

    await userEvent.click(screen.getByRole('link', { name: 'go to settings' }));
    expect(document.title).toBe('Settings · Ticket System Tech');
  });

  it('says so on a page that does not exist', () => {
    renderAt('/nowhere');
    expect(document.title).toBe('Page not found · Ticket System Tech');
  });

  it('declares the language of the page and follows the chosen one', async () => {
    renderAt('/tickets');
    expect(document.documentElement.lang).toBe('en');

    await userEvent.click(screen.getByRole('button', { name: 'bs' }));
    expect(document.documentElement.lang).toBe('bs');
    expect(document.title).toContain('Ticket System Tech');
  });
});
