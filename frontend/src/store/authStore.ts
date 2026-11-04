import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { UserSummary } from '../types';

interface AuthState {
  accessToken: string | null;
  user: UserSummary | null;
  setSession: (accessToken: string, user: UserSummary) => void;
  updateUser: (user: Partial<UserSummary>) => void;
  /** True after the person pressed "Sign out" (not after an expired session), so the sign-in page does not send the next person back to this one's last screen. Never persisted. */
  signedOutOnPurpose: boolean;
  /** True after the server stopped accepting the token while a person was signed in (not after "Sign out"): the sign-in page tells them why they are back. Never persisted. */
  sessionExpired: boolean;
  logout: () => void;
  signOut: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      user: null,
      signedOutOnPurpose: false,
      sessionExpired: false,
      setSession: (accessToken, user) => set({ accessToken, user, signedOutOnPurpose: false, sessionExpired: false }),
      updateUser: (partial) => set({ user: { ...get().user!, ...partial } }),
      logout: () => set({ accessToken: null, user: null, sessionExpired: get().accessToken !== null }),
      signOut: () => set({ accessToken: null, user: null, signedOutOnPurpose: true, sessionExpired: false }),
    }),
    {
      name: 'tst-auth',
      partialize: (s) => ({ accessToken: s.accessToken, user: s.user }),
    }
  )
);
