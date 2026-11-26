import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { employeeReportsApi, type PeriodParams } from './employeeReports';

/** One entry per selection, forgotten when the selection is left, like the other figures (see reportQueries.ts). */
const FIGURES = { gcTime: 0, placeholderData: keepPreviousData } as const;

export function useWorkload(params: { departmentId?: string }) {
  return useQuery({ queryKey: ['employee-reports', 'workload', params], queryFn: () => employeeReportsApi.workload(params), ...FIGURES });
}

export function useHoursOfPeople(params: PeriodParams & { departmentId?: string }) {
  return useQuery({ queryKey: ['employee-reports', 'hours', params], queryFn: () => employeeReportsApi.hours(params), ...FIGURES });
}

/** One person's hours; asked for only once a person is chosen. */
export function useHoursOfPerson(userId: string | undefined, params: PeriodParams) {
  return useQuery({
    queryKey: ['employee-reports', 'hours-of', userId, params],
    queryFn: () => employeeReportsApi.hoursOf(userId!, params),
    enabled: !!userId,
    ...FIGURES,
  });
}

export function useSpecialization(userId: string | undefined, params: PeriodParams) {
  return useQuery({
    queryKey: ['employee-reports', 'specialization', userId, params],
    queryFn: () => employeeReportsApi.specialization({ ...params, userId }),
    enabled: !!userId,
    ...FIGURES,
  });
}

/** For Admin only: the server refuses anybody else. */
export function useDependency(enabled: boolean) {
  return useQuery({ queryKey: ['employee-reports', 'dependency'], queryFn: employeeReportsApi.dependency, enabled, ...FIGURES });
}
