import { apiClient } from './client';
import type { DependencyDto, HoursDetailDto, HoursPeopleDto, SpecializationDto, WorkloadDto } from '../types';

export interface PeriodParams {
  from?: string;
  to?: string;
}

const base = '/api/reports/employees';

export const employeeReportsApi = {
  workload: (params: { departmentId?: string }) => apiClient.get<WorkloadDto>(`${base}/workload`, { params }).then((r) => r.data),
  hours: (params: PeriodParams & { departmentId?: string }) => apiClient.get<HoursPeopleDto>(`${base}/hours`, { params }).then((r) => r.data),
  hoursOf: (userId: string, params: PeriodParams) => apiClient.get<HoursDetailDto>(`${base}/hours/${userId}`, { params }).then((r) => r.data),
  specialization: (params: PeriodParams & { userId?: string }) => apiClient.get<SpecializationDto>(`${base}/specialization`, { params }).then((r) => r.data),
  dependency: () => apiClient.get<DependencyDto>(`${base}/dependency`).then((r) => r.data),

  /// Downloads the monthly worksheet of a person. The language is the one of the person who asks (the server knows it).
  timesheet: async (params: { userId: string; year: number; month: number }) => {
    const response = await apiClient.get(`${base}/timesheet`, { params, responseType: 'blob' });
    const disposition = response.headers['content-disposition'] as string | undefined;
    const filename = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? 'timesheet.xlsx';
    const url = window.URL.createObjectURL(new Blob([response.data]));
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  },
};
