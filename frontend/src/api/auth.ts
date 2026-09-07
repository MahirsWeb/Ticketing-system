import { apiClient } from './client';
import type { LoginResponse } from '../types';

export const authApi = {
  login: (email: string, password: string) =>
    apiClient.post<LoginResponse>('/api/auth/login', { email, password }).then((r) => r.data),

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
