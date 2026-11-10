import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { useAuthStore } from '../store/authStore';
import type { LoginResponse } from '../types';

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

// A single in-flight refresh shared by every request that hits a 401 at once: the cookie is rotated on each use, so two
// parallel refreshes would invalidate each other. The first starts it; the rest await the same promise.
let refreshing: Promise<string | null> | null = null;

/** Asks the server to trade the refresh cookie for a new access token. Updates the store on success. Returns the token, or null. */
export async function refreshAccessToken(): Promise<string | null> {
  if (!refreshing) {
    refreshing = axios
      .post<LoginResponse>(`${API_BASE_URL}/api/auth/refresh`, null, { withCredentials: true })
      .then((r) => {
        const data = r.data;
        if (data.accessToken && data.user) {
          useAuthStore.getState().setSession(data.accessToken, data.user);
          return data.accessToken;
        }
        return null;
      })
      .catch(() => null)
      .finally(() => {
        refreshing = null;
      });
  }
  return refreshing;
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
    const url = original?.url ?? '';
    // One silent refresh-and-retry per request. Never on the auth endpoints themselves (a failed refresh/login must not loop),
    // and only when a session actually exists to refresh.
    const isAuthCall = url.includes('/api/auth/');
    if (error.response?.status === 401 && original && !original._retried && !isAuthCall) {
      original._retried = true;
      const token = await refreshAccessToken();
      if (token) {
        original.headers.Authorization = `Bearer ${token}`;
        return apiClient(original);
      }
      useAuthStore.getState().logout();
    }
    return Promise.reject(error);
  }
);
