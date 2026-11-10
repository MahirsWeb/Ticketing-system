import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { knowledgeBaseApi } from '../../api/knowledgeBase';
import { startReindex, useReindexStatus } from '../../api/knowledgeBaseQueries';
import { Button, ErrorText } from '../../components/ui';
import { ReindexStatusError } from './ReindexStatusError';
import { Panel } from './settingsUi';
import { useLanguage } from '../../i18n/LanguageContext';

/** Embeds the tickets that still lack an entry, in the background; follows the run until it ends. */
export function KnowledgeBaseMaintenancePanel() {
  const { t } = useLanguage();
  const queryClient = useQueryClient();
  const run = useReindexStatus();
  const status = run.data;
  const [error, setError] = useState<string | null>(null);

  // A run that is already going (started earlier, or from another tab, or from the panel of a document) is picked up as soon as the panel opens.
  const running = status?.state === 'Running';

  async function handleReindex() {
    setError(null);
    setError(await startReindex(queryClient, knowledgeBaseApi.reindexAll, t('kbmaint_startFail')));
  }

  const progress = status && status.total > 0 ? `${status.processed} / ${status.total}` : null;

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">{t('kbmaint_title')}</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-600">
        AI search only picks up a ticket once it's been indexed — normally that happens automatically the moment a
        ticket is closed through the app. Tickets inserted directly (e.g. bulk-imported demo data) skip that step, so
        they won't show up in AI search or chat until you reindex. This embeds the tickets that still lack an entry —
        only ones with enough internal notes to ever be a search result — in the background, so you can leave this page.
        Safe to run any time, and it continues where it stopped.
      </p>
      <ErrorText>{error}</ErrorText>
      <ReindexStatusError run={run} />
      {status && status.state !== 'Idle' && (
        <p className={`mb-3 text-sm ${status.state === 'Failed' ? 'text-red-700' : status.state === 'Completed' ? 'text-green-700' : 'text-slate-700'}`}>
          {progress && <span className="font-semibold">{progress}</span>} {progress && '· '}
          {status.state === 'Running' ? t('kbmaint_running') : ''}
          {status.message}
          {status.embedded > 0 && status.state !== 'Running' ? ` (${status.embedded} embedded)` : ''}
        </p>
      )}
      <Button onClick={handleReindex} disabled={running}>
        {running ? t('kbmaint_reindexing') : t('kbmaint_reindexBtn')}
      </Button>
    </Panel>
  );
}
