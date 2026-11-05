import { apiClient } from './client';
import { queryClient } from './queryClient';
import { lookupKeys } from './lookupKeys';
import type { CompanyItemFull, DepartmentItemFull, LookupItem, LookupItemFull, SlaPlanItem, SlaPlanItemFull, SubBranchItemFull } from '../types';

/** A change to one of the lists makes every screen that has the list cached look at the new one the next time. */
function refreshed<T>(request: Promise<T>): Promise<T> {
  return request.then((result) => {
    void queryClient.invalidateQueries({ queryKey: lookupKeys.all });
    return result;
  });
}

export const lookupsApi = {
  companies: () => apiClient.get<CompanyItemFull[]>('/api/companies').then((r) => r.data),
  companiesAdmin: () => apiClient.get<CompanyItemFull[]>('/api/companies', { params: { includeInactive: true } }).then((r) => r.data),
  getCompany: (id: string) => apiClient.get<CompanyItemFull>(`/api/companies/${id}`).then((r) => r.data),
  createCompany: (name: string, address?: string, contactInfo?: string) =>
    refreshed(apiClient.post<LookupItem>('/api/companies', { name, address, contactInfo }).then((r) => r.data)),
  updateCompany: (id: string, name: string, address: string | undefined, contactInfo: string | undefined, isActive: boolean) =>
    refreshed(
      apiClient.put<CompanyItemFull>(`/api/companies/${id}`, { name, address, contactInfo, isActive }).then((r) => {
        // The company that is open takes what the server sent back at once; nothing is put into memory for one that is not.
        queryClient.setQueryData(lookupKeys.company(r.data.id), (open: CompanyItemFull | undefined) => (open ? r.data : undefined));
        return r.data;
      })
    ),

  // Branches (grane) — every authenticated user gets name + email; includeInactive is admin-only.
  departments: () => apiClient.get<DepartmentItemFull[]>('/api/departments').then((r) => r.data),
  departmentsAdmin: () =>
    apiClient.get<DepartmentItemFull[]>('/api/departments', { params: { includeInactive: true } }).then((r) => r.data),
  createDepartment: (name: string, email: string) =>
    refreshed(apiClient.post<DepartmentItemFull>('/api/departments', { name, email }).then((r) => r.data)),
  updateDepartment: (id: string, name: string, email: string, isActive: boolean) =>
    refreshed(apiClient.put<DepartmentItemFull>(`/api/departments/${id}`, { name, email, isActive }).then((r) => r.data)),
  deleteDepartment: (id: string) => refreshed(apiClient.delete(`/api/departments/${id}`).then((r) => r.data)),

  // Sub-branches (podgrane) — optional finer unit within a branch, sharing its email.
  subBranches: (departmentId: string) =>
    apiClient.get<SubBranchItemFull[]>(`/api/departments/${departmentId}/sub-branches`).then((r) => r.data),
  subBranchesAdmin: (departmentId: string) =>
    apiClient.get<SubBranchItemFull[]>(`/api/departments/${departmentId}/sub-branches`, { params: { includeInactive: true } }).then((r) => r.data),
  createSubBranch: (departmentId: string, name: string) =>
    refreshed(apiClient.post<SubBranchItemFull>(`/api/departments/${departmentId}/sub-branches`, { name }).then((r) => r.data)),
  updateSubBranch: (id: string, name: string, isActive: boolean) =>
    refreshed(apiClient.put<SubBranchItemFull>(`/api/sub-branches/${id}`, { name, isActive }).then((r) => r.data)),

  helpTopics: () => apiClient.get<LookupItem[]>('/api/help-topics').then((r) => r.data),
  helpTopicsAdmin: () =>
    apiClient.get<LookupItemFull[]>('/api/help-topics', { params: { includeInactive: true } }).then((r) => r.data),
  createHelpTopic: (name: string) => refreshed(apiClient.post<LookupItemFull>('/api/help-topics', { name }).then((r) => r.data)),
  updateHelpTopic: (id: string, name: string, isActive: boolean) =>
    refreshed(apiClient.put<LookupItemFull>(`/api/help-topics/${id}`, { name, isActive }).then((r) => r.data)),
  deleteHelpTopic: (id: string) => refreshed(apiClient.delete(`/api/help-topics/${id}`).then((r) => r.data)),

  // Ticket categories — internal-only classification, staff/Admin only (clients never see this list).
  ticketCategories: () => apiClient.get<LookupItem[]>('/api/ticket-categories').then((r) => r.data),
  ticketCategoriesAdmin: () =>
    apiClient.get<LookupItemFull[]>('/api/ticket-categories', { params: { includeInactive: true } }).then((r) => r.data),
  createTicketCategory: (name: string) => refreshed(apiClient.post<LookupItemFull>('/api/ticket-categories', { name }).then((r) => r.data)),
  updateTicketCategory: (id: string, name: string, isActive: boolean) =>
    refreshed(apiClient.put<LookupItemFull>(`/api/ticket-categories/${id}`, { name, isActive }).then((r) => r.data)),
  deleteTicketCategory: (id: string) => refreshed(apiClient.delete(`/api/ticket-categories/${id}`).then((r) => r.data)),

  slaPlans: () => apiClient.get<SlaPlanItem[]>('/api/sla-plans').then((r) => r.data),
  slaPlansAdmin: () =>
    apiClient.get<SlaPlanItemFull[]>('/api/sla-plans', { params: { includeInactive: true } }).then((r) => r.data),
  createSlaPlan: (name: string, resolutionTimeHours: number) =>
    refreshed(apiClient.post<SlaPlanItemFull>('/api/sla-plans', { name, resolutionTimeHours }).then((r) => r.data)),
  updateSlaPlan: (id: string, name: string, resolutionTimeHours: number, isActive: boolean) =>
    refreshed(apiClient.put<SlaPlanItemFull>(`/api/sla-plans/${id}`, { name, resolutionTimeHours, isActive }).then((r) => r.data)),
  deleteSlaPlan: (id: string) => refreshed(apiClient.delete(`/api/sla-plans/${id}`).then((r) => r.data)),
};
