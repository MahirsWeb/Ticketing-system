import { useQuery } from '@tanstack/react-query';
import { knowledgeBaseApi } from './knowledgeBase';
import { knowledgeBaseKeys } from './knowledgeBaseKeys';

export { knowledgeBaseKeys };

/**
 * How common a problem is: the tickets that look like the described one, and their share of the whole base. Asked for each text, and for
 * nothing when there is no text. The server makes it by weighing every documented ticket against the text (and has the text embedded by an
 * outside service), so it is never shown from memory, and a request that is still being made is cancelled when the page no longer wants it.
 */
export function useSimilarTickets(query: string) {
  return useQuery({
    queryKey: knowledgeBaseKeys.similar(query),
    queryFn: ({ signal }) => knowledgeBaseApi.similarTickets(query, signal),
    enabled: query.trim() !== '',
    gcTime: 0,
  });
}
