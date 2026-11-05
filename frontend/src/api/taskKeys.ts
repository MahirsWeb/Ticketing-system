/** Names of the cached task data; see taskQueries.ts. */
export const taskKeys = {
  all: ['tasks'] as const,
  /** Every list, whatever it is narrowed by. */
  lists: ['tasks', 'list'] as const,
  list: (params: object) => ['tasks', 'list', params] as const,
  detail: (id: string) => ['tasks', 'detail', id] as const,
};
