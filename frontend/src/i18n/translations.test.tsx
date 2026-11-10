import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { LanguageProvider } from './LanguageContext';
import { translations } from './translations';
import ForgotPasswordPage from '../pages/auth/ForgotPasswordPage';

describe('translations', () => {
  it('every English key has a Bosnian counterpart, and vice versa', () => {
    const en = Object.keys(translations.en).sort();
    const bs = Object.keys(translations.bs).sort();
    expect(bs).toEqual(en); // a screen is never half-translated: a missing key would fall back to English silently
  });
});

describe('a translated screen', () => {
  beforeEach(() => localStorage.clear());

  it('shows English by default', () => {
    render(
      <MemoryRouter>
        <LanguageProvider>
          <ForgotPasswordPage />
        </LanguageProvider>
      </MemoryRouter>
    );
    expect(screen.getByRole('heading', { name: 'Forgot password' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send reset link' })).toBeInTheDocument();
  });

  it('shows Bosnian when that is the chosen language', () => {
    localStorage.setItem('tst-language', 'bs');
    render(
      <MemoryRouter>
        <LanguageProvider>
          <ForgotPasswordPage />
        </LanguageProvider>
      </MemoryRouter>
    );
    expect(screen.getByRole('heading', { name: 'Zaboravljena lozinka' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pošalji link za resetovanje' })).toBeInTheDocument();
  });
});
