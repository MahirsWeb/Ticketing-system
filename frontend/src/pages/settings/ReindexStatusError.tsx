import type { UseQueryResult } from '@tanstack/react-query';
import type { ReindexStatus } from '../../api/knowledgeBase';
import { Button, ErrorText } from '../../components/ui';

/** What to say when how the run is going cannot be found out: nothing known yet (with a way to ask again), or a run that is known and cannot be followed just now. */
export function ReindexStatusError({ run }: { run: UseQueryResult<ReindexStatus> }) {
  if (!run.isError) return null;
  if (run.data) return <ErrorText>The progress of the reindex could not be checked just now; it is asked for again.</ErrorText>;
  return (
    <>
      <ErrorText>The state of the reindex could not be loaded.</ErrorText>
      <Button variant="secondary" className="mt-2" onClick={() => void run.refetch()}>
        Try again
      </Button>
    </>
  );
}
