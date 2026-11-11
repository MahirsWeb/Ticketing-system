import { create } from 'zustand';
import type { UserSummary } from '../types';

interface AuthState {
  /** The short-lived access token. Kept in memory ONLY (never localStorage): it is not in any storage that a copy of the browser profile or a script
   *  that reads storage would find, and it is gone when the tab closes. It does NOT make the session unreachable for a script running in the page
   *  itself: such a script can ask the server for a fresh token (the httpOnly cookie goes along by itself). Against that, sanitising and the CSP are
   *  what protect. The long-lived half lives in an httpOnly cookie the server sets. */
  accessToken: string | null;
  user: UserSummary | null;
  /** True until the one-time "do I still have a session?" refresh on page load has finished. While true the app shows a
   *  loader instead of bouncing to the sign-in page, so a reload does not look like a sign-out. */
  initializing: boolean;
  setSession: (accessToken: string, user: UserSummary) => void;
  updateUser: (user: Partial<UserSummary>) => void;
  finishInitializing: () => void;
  /** True after the person pressed "Sign out" (not after an expired session), so the sign-in page does not send the next person back to this one's last screen. */
  signedOutOnPurpose: boolean;
  /** Counted up every time a session ends. A renewal that was already under way when that happened is thrown away when it answers, so it cannot
   *  bring back a session the person has just left. */
  epoch: number;
  /** True after the server stopped accepting the session while a person was signed in (not after "Sign out"): the sign-in page tells them why they are back. */
  sessionExpired: boolean;
  /** Clears the in-memory session because the server stopped accepting it (an expired/again-rejected refresh). */
  logout: () => void;
  /** Clears the in-memory session because the person chose to sign out. */
  signOut: () => void;
}

// The previous version kept the whole session (an access token and the person's e-mail) in localStorage. Nothing reads or writes
// it any more, so it is removed once at start-up rather than left behind for any script on the page to read.
try {
  localStorage.removeItem('tst-auth');
} catch {
  /* storage unavailable (private mode, blocked): nothing to remove */
}

export const useAuthStore = create<AuthState>()((set, get) => ({
  accessToken: null,
  user: null,
  initializing: true,
  signedOutOnPurpose: false,
  epoch: 0,
  sessionExpired: false,
  setSession: (accessToken, user) => set({ accessToken, user, signedOutOnPurpose: false, sessionExpired: false }),
  updateUser: (partial) => set({ user: { ...get().user!, ...partial } }),
  finishInitializing: () => set({ initializing: false }),
  logout: () => set({ accessToken: null, user: null, sessionExpired: get().accessToken !== null, epoch: get().epoch + 1 }),
  signOut: () => set({ accessToken: null, user: null, signedOutOnPurpose: true, sessionExpired: false, epoch: get().epoch + 1 }),
}));
