import { beforeEach, describe, expect, it } from 'vitest';
import { useAuthStore } from './authStore';
import type { UserSummary } from '../types';

const user = { id: 'u1', firstName: 'Ana', lastName: 'Test', email: 'ana@example.test', role: 'Employee' } as UserSummary;

describe('authStore', () => {
  beforeEach(() => useAuthStore.setState({ accessToken: null, user: null, signedOutOnPurpose: false }));

  it('keeps the session after sign-in', () => {
    useAuthStore.getState().setSession('token', user);

    expect(useAuthStore.getState().accessToken).toBe('token');
    expect(useAuthStore.getState().user?.id).toBe('u1');
  });

  it('forgets the session when it expires, without calling it a deliberate sign-out', () => {
    useAuthStore.getState().setSession('token', user);

    useAuthStore.getState().logout();

    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(useAuthStore.getState().signedOutOnPurpose).toBe(false);
  });

  it('remembers that the person signed out on purpose, until somebody signs in again', () => {
    useAuthStore.getState().setSession('token', user);
    useAuthStore.getState().signOut();
    expect(useAuthStore.getState().signedOutOnPurpose).toBe(true);

    useAuthStore.getState().setSession('token2', user);
    expect(useAuthStore.getState().signedOutOnPurpose).toBe(false);
  });

  it('never writes the session to browser storage — the access token lives in memory only', () => {
    localStorage.clear();
    useAuthStore.getState().setSession('token', user);
    useAuthStore.getState().signOut();

    // Nothing about the session is persisted: a script that reads localStorage finds no token to steal, and the refresh
    // cookie it would need instead is httpOnly. (The session is restored on load from that cookie, not from storage.)
    expect(localStorage.getItem('tst-auth')).toBeNull();
    expect(localStorage.length).toBe(0);
  });
});

describe('an expired session', () => {
  it('is remembered, so the sign-in page can say why the person is back, and forgotten by a new sign-in', () => {
    useAuthStore.setState({ accessToken: 'token', user: { id: 'u1' } as never, signedOutOnPurpose: false, sessionExpired: false });

    useAuthStore.getState().logout(); // what a 401 does

    expect(useAuthStore.getState().sessionExpired).toBe(true);
    useAuthStore.getState().setSession('new-token', { id: 'u1' } as never);
    expect(useAuthStore.getState().sessionExpired).toBe(false);
  });

  it('is not claimed when the person signed out themselves, or was never signed in', () => {
    useAuthStore.setState({ accessToken: 'token', user: { id: 'u1' } as never, sessionExpired: false });
    useAuthStore.getState().signOut();
    expect(useAuthStore.getState().sessionExpired).toBe(false);

    useAuthStore.getState().logout(); // a 401 for a request made without a session
    expect(useAuthStore.getState().sessionExpired).toBe(false);
  });
});
