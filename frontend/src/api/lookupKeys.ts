/** Names of the cached lists; see lookupQueries.ts. */
export const lookupKeys = {
  all: ['lookups'] as const,
  departments: ['lookups', 'departments'] as const,
  companies: ['lookups', 'companies'] as const,
  helpTopics: ['lookups', 'help-topics'] as const,
  slaPlans: ['lookups', 'sla-plans'] as const,
  ticketCategories: ['lookups', 'ticket-categories'] as const,
  subBranches: (departmentId: string) => ['lookups', 'sub-branches', departmentId] as const,
};
