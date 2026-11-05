import { useEffect, useState } from 'react';
import { knowledgeBaseApi, type ReindexStatus } from '../../api/knowledgeBase';
import { Button, ErrorText } from '../../components/ui';
import { messageOf } from './settingsHelpers';
import { Panel } from './settingsUi';

/** Embeds the tickets that still lack an entry, in the background; follows the run until it ends. */
export function KnowledgeBaseMaintenancePanel() {
  const [status, setStatus] = useState<ReindexStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  const running = status?.state === 'Running';

  // Picks up a run that is already in progress (started earlier, or from another tab) as soon as the panel opens.
  useEffect(() => {
    knowledgeBaseApi.reindexStatus().then(setStatus).catch(() => undefined);
  }, []);

  // The reindex runs on the server in the background; poll until it finishes.
  useEffect(() => {
    if (!running) return;
    const timer = window.setInterval(() => {
      knowledgeBaseApi.reindexStatus().then(setStatus).catch(() => undefined);
    }, 3000);
    return () => window.clearInterval(timer);
  }, [running]);

  async function handleReindex() {
    setError(null);
    try {
      setStatus(await knowledgeBaseApi.reindexAll());
    } catch (err: any) {
      if (err?.response?.status === 409 && err.response.data?.status) {
        setStatus(err.response.data.status as ReindexStatus);
      } else {
        setError(messageOf(err, 'Could not start the reindex.'));
      }
    }
  }

  const progress = status && status.total > 0 ? `${status.processed} / ${status.total}` : null;

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">Knowledge base</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-500">
        AI search only picks up a ticket once it's been indexed — normally that happens automatically the moment a
        ticket is closed through the app. Tickets inserted directly (e.g. bulk-imported demo data) skip that step, so
        they won't show up in AI search or chat until you reindex. This embeds the tickets that still lack an entry —
        only ones with enough internal notes to ever be a search result — in the background, so you can leave this page.
        Safe to run any time, and it continues where it stopped.
      </p>
      <ErrorText>{error}</ErrorText>
      {status && status.state !== 'Idle' && (
        <p className={`mb-3 text-sm ${status.state === 'Failed' ? 'text-red-700' : status.state === 'Completed' ? 'text-green-700' : 'text-slate-700'}`}>
          {progress && <span className="font-semibold">{progress}</span>} {progress && '· '}
          {status.state === 'Running' ? 'Running… ' : ''}
          {status.message}
          {status.embedded > 0 && status.state !== 'Running' ? ` (${status.embedded} embedded)` : ''}
        </p>
      )}
      <Button onClick={handleReindex} disabled={running}>
        {running ? 'Reindexing…' : 'Reindex missing tickets'}
      </Button>
    </Panel>
  );
}
