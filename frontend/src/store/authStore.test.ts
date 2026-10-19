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

  it('stores only the session in the browser, never the sign-out marker', () => {
    useAuthStore.getState().setSession('token', user);
    useAuthStore.getState().signOut();

    const stored = JSON.parse(localStorage.getItem('tst-auth')!);
    expect(Object.keys(stored.state).sort()).toEqual(['accessToken', 'user']);
  });
});
