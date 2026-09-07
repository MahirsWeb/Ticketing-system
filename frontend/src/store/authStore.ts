import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { UserSummary } from '../types';

interface AuthState {
  accessToken: string | null;
  user: UserSummary | null;
  setSession: (accessToken: string, user: UserSummary) => void;
  updateUser: (user: Partial<UserSummary>) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      user: null,
      setSession: (accessToken, user) => set({ accessToken, user }),
      updateUser: (partial) => set({ user: { ...get().user!, ...partial } }),
      logout: () => set({ accessToken: null, user: null }),
    }),
    { name: 'tst-auth' }
  )
);
