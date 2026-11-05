/** Names of the cached account data; see userQueries.ts. */
export const userKeys = {
  all: ['users'] as const,
  employees: (departmentId: string | null) => ['users', 'employees', departmentId] as const,
  detail: (id: string) => ['users', 'detail', id] as const,
  tickets: (role: string, id: string) => ['users', 'tickets', role, id] as const,
  /** The clients of one company, for the page of the company. */
  companyClients: (companyId: string) => ['users', 'company-clients', companyId] as const,
  /** Every page of the list of accounts, whatever it is for. */
  pages: ['users', 'page'] as const,
  page: (params: object) => ['users', 'page', params] as const,
};
