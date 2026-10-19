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
  logout: () => void;
  signOut: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      user: null,
      signedOutOnPurpose: false,
      setSession: (accessToken, user) => set({ accessToken, user, signedOutOnPurpose: false }),
      updateUser: (partial) => set({ user: { ...get().user!, ...partial } }),
      logout: () => set({ accessToken: null, user: null }),
      signOut: () => set({ accessToken: null, user: null, signedOutOnPurpose: true }),
    }),
    {
      name: 'tst-auth',
      partialize: (s) => ({ accessToken: s.accessToken, user: s.user }),
    }
  )
);
