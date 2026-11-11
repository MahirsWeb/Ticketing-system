import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { useAuthStore } from '../store/authStore';
import type { LoginResponse } from '../types';
import { storedLanguage } from '../i18n/translate';
import { translateServerMessage } from '../i18n/serverMessages';

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  // Send and receive the httpOnly refresh cookie on the /api/auth calls that use it.
  withCredentials: true,
});

apiClient.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

/// The server could not be asked, or could not answer right now (no network, 429, 5xx): the session may well still be good.
/// Not the same as being refused, and callers must not sign the person out for it.
export class RefreshUnavailableError extends Error {
  constructor() {
    super('The session could not be renewed right now.');
    this.name = 'RefreshUnavailableError';
  }
}

/// Waits before trying again after a transient failure. Replaceable so tests need not wait.
export const refreshRetry = {
  delaysMs: [1000, 3000, 8000],
  sleep: (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)),
};

/// The refresh cookie rotates on every use, so two browser tabs that renew at the same moment would spend the same cookie twice.
/// A lock shared by every tab of this site makes them take turns: the second one sends the cookie the first one just received.
/// (Browsers without Web Locks fall back to no lock; the server forgives a second use of a cookie within seconds.)
function withCrossTabLock<T>(work: () => Promise<T>): Promise<T> {
  const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined;
  return locks ? locks.request('tst-refresh', work) : work();
}

type Attempt = { kind: 'token'; token: string } | { kind: 'refused' } | { kind: 'retry' } | { kind: 'unavailable' };

async function attemptRefresh(): Promise<Attempt> {
  try {
    const r = await withCrossTabLock(() =>
      axios.post<LoginResponse>(`${API_BASE_URL}/api/auth/refresh`, null, { withCredentials: true })
    );
    const data = r.data;
    if (data.accessToken && data.user) {
      useAuthStore.getState().setSession(data.accessToken, data.user);
      return { kind: 'token', token: data.accessToken };
    }
    return { kind: 'refused' };
  } catch (err) {
    const status = (err as AxiosError).response?.status;
    if (status === 401) return { kind: 'refused' }; // the only answer that means "this session is over"
    if (status === undefined || status === 429 || status === 408 || status >= 500) return { kind: 'retry' };
    return { kind: 'unavailable' }; // some other refusal (for example a blocked origin): retrying will not change it
  }
}

// A single in-flight refresh shared by every request that hits a 401 at once in this tab.
let refreshing: Promise<string | null> | null = null;

/// Asks the server to trade the refresh cookie for a new access token. Updates the store on success. Returns the token, or null
/// when the server REFUSED the cookie (the session is over). Throws RefreshUnavailableError when it could not be asked or could not
/// answer (after a few tries, spaced out): that is not a sign-out.
export async function refreshAccessToken(): Promise<string | null> {
  if (!refreshing) {
    refreshing = (async () => {
      for (let attempt = 0; ; attempt++) {
        const result = await attemptRefresh();
        if (result.kind === 'token') return result.token;
        if (result.kind === 'refused') return null;
        if (result.kind === 'unavailable' || attempt >= refreshRetry.delaysMs.length) throw new RefreshUnavailableError();
        await refreshRetry.sleep(refreshRetry.delaysMs[attempt]);
      }
    })().finally(() => {
      refreshing = null;
    });
  }
  return refreshing;
}

/// The server's own words (a refusal, or a confirmation) arrive in English; they are put into the language of the screen here, once, so that
/// no screen has to. Anything that is not in the list of known messages is left exactly as it came.
function translateAnswer(data: unknown) {
  if (data && typeof data === 'object' && typeof (data as { message?: unknown }).message === 'string') {
    const answer = data as { message: string };
    answer.message = translateServerMessage(answer.message, storedLanguage());
  }
}

apiClient.interceptors.response.use(
  (response) => {
    translateAnswer(response.data);
    return response;
  },
  async (error: AxiosError) => {
    translateAnswer(error.response?.data);
    const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
    const url = original?.url ?? '';
    // One silent refresh-and-retry per request. Never on the auth endpoints themselves (a failed refresh/login must not loop),
    // and only for a request that carried a session: one sent without a token (the sign-in page, or after signing out) has no
    // session to renew, and renewing for it could hand a session back to somebody who just left.
    const isAuthCall = url.includes('/api/auth/');
    const carriedSession = Boolean(original?.headers?.Authorization);
    if (error.response?.status === 401 && original && !original._retried && !isAuthCall && carriedSession) {
      original._retried = true;
      try {
        const token = await refreshAccessToken();
        if (token) {
          original.headers.Authorization = `Bearer ${token}`;
          return apiClient(original);
        }
        useAuthStore.getState().logout(); // refused: the session really is over
      } catch (refreshError) {
        // Could not renew right now (network, rate limit, server trouble): the person stays signed in and this request simply failed.
        if (!(refreshError instanceof RefreshUnavailableError)) throw refreshError;
      }
    }
    return Promise.reject(error);
  }
);
