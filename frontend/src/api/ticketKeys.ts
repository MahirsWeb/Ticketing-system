/** Names of the cached ticket data; see ticketQueries.ts. */
export const ticketKeys = {
  all: ['tickets'] as const,
  /** Every list, whatever it is filtered by. */
  lists: ['tickets', 'list'] as const,
  list: (params: object) => ['tickets', 'list', params] as const,
  counts: ['tickets', 'counts'] as const,
  detail: (id: string) => ['tickets', 'detail', id] as const,
};
