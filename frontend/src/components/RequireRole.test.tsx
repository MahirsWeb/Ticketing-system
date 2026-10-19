import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { RequireAuth, RequireRole } from './RequireRole';
import { useAuthStore } from '../store/authStore';
import type { UserSummary } from '../types';

function SignIn() {
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from;
  return <p>sign-in page, came from: {from ?? 'nowhere'}</p>;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/login" element={<SignIn />} />
        <Route
          path="/tickets"
          element={
            <RequireAuth>
              <p>the tickets</p>
            </RequireAuth>
          }
        />
        <Route
          path="/settings"
          element={
            <RequireRole roles={['Admin']}>
              <p>the settings</p>
            </RequireRole>
          }
        />
        <Route path="/" element={<p>start page</p>} />
      </Routes>
    </MemoryRouter>
  );
}

const employee = { id: 'u1', role: 'Employee' } as UserSummary;

describe('RequireAuth', () => {
  beforeEach(() => useAuthStore.setState({ accessToken: null, user: null, signedOutOnPurpose: false }));

  it('lets a signed-in person through', () => {
    useAuthStore.setState({ accessToken: 't', user: employee });
    renderAt('/tickets');
    expect(screen.getByText('the tickets')).toBeInTheDocument();
  });

  it('sends a visitor to sign in and remembers where they were going', () => {
    renderAt('/tickets?status=New');
    expect(screen.getByText('sign-in page, came from: /tickets?status=New')).toBeInTheDocument();
  });

  it('does not remember the page after a deliberate sign-out', () => {
    useAuthStore.setState({ accessToken: null, user: null, signedOutOnPurpose: true });
    renderAt('/tickets');
    expect(screen.getByText('sign-in page, came from: nowhere')).toBeInTheDocument();
  });
});

describe('RequireRole', () => {
  beforeEach(() => useAuthStore.setState({ accessToken: null, user: null, signedOutOnPurpose: false }));

  it('lets the right role in', () => {
    useAuthStore.setState({ accessToken: 't', user: { ...employee, role: 'Admin' } });
    renderAt('/settings');
    expect(screen.getByText('the settings')).toBeInTheDocument();
  });

  it('sends the wrong role to the start page', () => {
    useAuthStore.setState({ accessToken: 't', user: employee });
    renderAt('/settings');
    expect(screen.getByText('start page')).toBeInTheDocument();
  });
});
