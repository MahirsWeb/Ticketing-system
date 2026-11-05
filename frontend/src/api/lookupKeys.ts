/** Names of the cached lists; see lookupQueries.ts and adminLookupQueries.ts. */
export const lookupKeys = {
  all: ['lookups'] as const,
  departments: ['lookups', 'departments'] as const,
  companies: ['lookups', 'companies'] as const,
  /** One company, with all its details (the page of a company). */
  company: (id: string) => ['lookups', 'company', id] as const,
  helpTopics: ['lookups', 'help-topics'] as const,
  slaPlans: ['lookups', 'sla-plans'] as const,
  ticketCategories: ['lookups', 'ticket-categories'] as const,
  subBranches: (departmentId: string) => ['lookups', 'sub-branches', departmentId] as const,
  // The same lists with what is switched off in them, for the settings page.
  departmentsAdmin: ['lookups', 'departments', 'admin'] as const,
  companiesAdmin: ['lookups', 'companies', 'admin'] as const,
  helpTopicsAdmin: ['lookups', 'help-topics', 'admin'] as const,
  slaPlansAdmin: ['lookups', 'sla-plans', 'admin'] as const,
  ticketCategoriesAdmin: ['lookups', 'ticket-categories', 'admin'] as const,
  subBranchesAdmin: (departmentId: string) => ['lookups', 'sub-branches', departmentId, 'admin'] as const,
};
