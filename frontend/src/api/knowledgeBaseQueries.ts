import { useQuery, type QueryClient } from '@tanstack/react-query';
import { messageOf } from '../lib/messageOf';
import { knowledgeBaseApi, type ReindexStatus } from './knowledgeBase';
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

/** How often a run that is going is asked about. */
export const REINDEX_POLL_MS = 2000;

/**
 * How the background run that rebuilds what the AI assistant reads is going (the server has one run at a time, whoever started it, so every
 * panel that shows or starts one looks at this). While a run is going it is asked about again and again, never twice at once, and when it
 * is over, or the answer does not come, the asking stops or goes on by itself. Never shown from memory: somebody else may have started a run.
 */
export function useReindexStatus() {
  return useQuery({
    queryKey: knowledgeBaseKeys.reindexStatus,
    queryFn: ({ signal }) => knowledgeBaseApi.reindexStatus(signal),
    refetchInterval: (query) => (query.state.data?.state === 'Running' ? REINDEX_POLL_MS : false),
    gcTime: 0,
  });
}

/** What the server said about a run that is going: shown at once by whoever looks at it, and followed from there. */
async function reindexStatusKnown(queryClient: QueryClient, status: ReindexStatus) {
  const key = knowledgeBaseKeys.reindexStatus;
  // A question asked before the run was started may still be on its way, and its answer ("idle") would be taken for the newest news.
  await queryClient.cancelQueries({ queryKey: key });
  if (queryClient.getQueryCache().find({ queryKey: key })) queryClient.setQueryData(key, status);
}

/** The refusal of a run to start because one is already going says how far that one is. */
function runThatIsGoing(err: unknown): ReindexStatus | undefined {
  const refusal = (err as { response?: { status?: number; data?: { status?: ReindexStatus } } } | null)?.response;
  return refusal?.status === 409 ? refusal.data?.status : undefined;
}

/**
 * Starts a run with `start` and lets every panel see how it is going. Gives back why it could not be started, or null. A run that was
 * already going is not a failure: it is shown as it is, and followed.
 */
export async function startReindex(queryClient: QueryClient, start: () => Promise<ReindexStatus>, failure: string): Promise<string | null> {
  let said: ReindexStatus | undefined;
  try {
    said = await start();
  } catch (err) {
    said = runThatIsGoing(err);
    if (!said) return messageOf(err, failure);
  }
  await reindexStatusKnown(queryClient, said);
  return null;
}
