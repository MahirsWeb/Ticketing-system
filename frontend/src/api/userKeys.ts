/** Names of the cached account data; see userQueries.ts. */
export const userKeys = {
  all: ['users'] as const,
  /** The staff of a branch, or of a sub-branch of it, or all of them. */
  employees: (departmentId: string | null, subBranchId: string | null = null) => ['users', 'employees', departmentId, subBranchId] as const,
  /** The client found for one e-mail address (the sender of an e-mail a ticket is made from). */
  clientByEmail: (email: string) => ['users', 'client-by-email', email] as const,
  detail: (id: string) => ['users', 'detail', id] as const,
  tickets: (role: string, id: string) => ['users', 'tickets', role, id] as const,
  /** The clients of one company, for the page of the company. */
  companyClients: (companyId: string) => ['users', 'company-clients', companyId] as const,
  /** The clients of one company for the second picker of the ticket form (every branch sees the same list; not the list of the company's page). */
  companyClientChoices: (companyId: string) => ['users', 'company-client-choices', companyId] as const,
  /** Every page of the list of accounts, whatever it is for. */
  pages: ['users', 'page'] as const,
  page: (params: object) => ['users', 'page', params] as const,
};
