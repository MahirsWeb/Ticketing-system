import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LoginPage from './LoginPage';
import { LanguageProvider } from '../../i18n/LanguageContext';
import { useAuthStore } from '../../store/authStore';

function renderLogin() {
  return render(
    <LanguageProvider>
      <MemoryRouter initialEntries={['/login']}>
        <LoginPage />
      </MemoryRouter>
    </LanguageProvider>
  );
}

describe('the sign-in page', () => {
  beforeEach(() => useAuthStore.setState({ accessToken: null, user: null, signedOutOnPurpose: false, sessionExpired: false }));

  it('tells a person whose session ran out why they are back, and that the reply they were writing is kept (TS-18.6)', () => {
    useAuthStore.setState({ sessionExpired: true });

    renderLogin();

    expect(screen.getByRole('status')).toHaveTextContent('Your session has expired');
    expect(screen.getByRole('status')).toHaveTextContent('kept');
  });

  it('says nothing of the kind to somebody who simply opened it', () => {
    renderLogin();

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
