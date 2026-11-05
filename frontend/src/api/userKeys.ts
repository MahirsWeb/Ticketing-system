/** Names of the cached account data; see userQueries.ts. */
export const userKeys = {
  all: ['users'] as const,
  /** The staff of a branch, or of a sub-branch of it, or all of them. */
  employees: (departmentId: string | null, subBranchId: string | null = null) => ['users', 'employees', departmentId, subBranchId] as const,
  detail: (id: string) => ['users', 'detail', id] as const,
  tickets: (role: string, id: string) => ['users', 'tickets', role, id] as const,
  /** The clients of one company, for the page of the company. */
  companyClients: (companyId: string) => ['users', 'company-clients', companyId] as const,
  /** Every page of the list of accounts, whatever it is for. */
  pages: ['users', 'page'] as const,
  page: (params: object) => ['users', 'page', params] as const,
};
