/** Names of the cached knowledge base data; see knowledgeBaseQueries.ts. */
export const knowledgeBaseKeys = {
  all: ['knowledge-base'] as const,
  similar: (query: string) => ['knowledge-base', 'similar', query] as const,
};
