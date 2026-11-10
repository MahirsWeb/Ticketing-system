import { apiClient } from './client';
import type { LoginResponse } from '../types';

export const authApi = {
  login: (email: string, password: string) =>
    apiClient.post<LoginResponse>('/api/auth/login', { email, password }).then((r) => r.data),

  /** Trades the httpOnly refresh cookie for a fresh access token + user. Used on page load and after a 401. */
  refresh: () => apiClient.post<LoginResponse>('/api/auth/refresh', null).then((r) => r.data),

  /** Revokes this browser's refresh token server-side and clears the cookie. Best-effort: ignores failure. */
  logout: () => apiClient.post('/api/auth/logout', null).then((r) => r.data).catch(() => undefined),

  setNewPassword: (email: string, temporaryPassword: string, newPassword: string, confirmNewPassword: string) =>
    apiClient
      .post('/api/auth/set-new-password', { email, temporaryPassword, newPassword, confirmNewPassword })
      .then((r) => r.data),

  verifyEmail: (userId: string, token: string) =>
    apiClient.post('/api/auth/verify-email', { userId, token }).then((r) => r.data),

  forgotPassword: (email: string) => apiClient.post('/api/auth/forgot-password', { email }).then((r) => r.data),

  resetPassword: (userId: string, token: string, newPassword: string, confirmNewPassword: string) =>
    apiClient
      .post('/api/auth/reset-password', { userId, token, newPassword, confirmNewPassword })
      .then((r) => r.data),
};
