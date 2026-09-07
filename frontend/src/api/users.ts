import { apiClient } from './client';
import type { ClientLookupResult, CreatedUserResponse, UserListItemDto, UserRole } from '../types';

export const usersApi = {
  lookupByEmail: (email: string) =>
    apiClient.get<ClientLookupResult>('/api/users/lookup-by-email', { params: { email } }).then((r) => r.data),

  createEmployee: (firstName: string, lastName: string, email: string, role: UserRole, departmentId?: string, subBranchId?: string) =>
    apiClient
      .post<CreatedUserResponse>('/api/users/employees', {
        firstName,
        lastName,
        email,
        role,
        departmentId: departmentId || null,
        subBranchId: subBranchId || null,
      })
      .then((r) => r.data),

  createClient: (firstName: string, lastName: string, email: string, companyId: string) =>
    apiClient.post<CreatedUserResponse>('/api/users/clients', { firstName, lastName, email, companyId }).then((r) => r.data),

  regenerateTempPassword: (userId: string) =>
    apiClient.post<CreatedUserResponse>(`/api/users/${userId}/regenerate-temp-password`).then((r) => r.data),

  list: (params?: { role?: UserRole; companyId?: string; departmentId?: string; subBranchId?: string }) =>
    apiClient.get<UserListItemDto[]>('/api/users', { params }).then((r) => r.data),

  getById: (userId: string) => apiClient.get<UserListItemDto>(`/api/users/${userId}`).then((r) => r.data),

  updateUser: (
    userId: string,
    data: { firstName: string; lastName: string; role: UserRole; companyId?: string; departmentId?: string; subBranchId?: string }
  ) =>
    apiClient
      .put(`/api/users/${userId}`, {
        firstName: data.firstName,
        lastName: data.lastName,
        role: data.role,
        companyId: data.companyId || null,
        departmentId: data.departmentId || null,
        subBranchId: data.subBranchId || null,
      })
      .then((r) => r.data),

  setActive: (userId: string, isActive: boolean) =>
    apiClient.patch(`/api/users/${userId}/active`, { isActive }).then((r) => r.data),

  uploadProfilePicture: (userId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return apiClient
      .post<{ profilePictureUrl: string }>(`/api/users/${userId}/profile-picture`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data);
  },

  setMyPhone: (phoneNumber: string) => apiClient.patch('/api/users/me/phone', { phoneNumber }).then((r) => r.data),

  skipPhonePrompt: () => apiClient.post('/api/users/me/skip-phone-prompt').then((r) => r.data),
};
