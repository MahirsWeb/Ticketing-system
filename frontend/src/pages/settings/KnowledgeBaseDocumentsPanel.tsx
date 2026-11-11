import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { knowledgeBaseApi } from '../../api/knowledgeBase';
import { startReindex, useReindexStatus } from '../../api/knowledgeBaseQueries';
import { settingsKeys, useKnowledgeBaseDocuments } from '../../api/settingsQueries';
import { ErrorText } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { KnowledgeBaseDocumentDto } from '../../types';
import { messageOf } from './settingsHelpers';
import { ReindexStatusError } from './ReindexStatusError';
import { ListRow, Panel } from './settingsUi';
import { useLanguage } from '../../i18n/LanguageContext';
import { translateServerMessage } from '../../i18n/serverMessages';

/** The manuals and instructions the AI assistant answers from: uploaded, removed, and rebuilt from their stored originals. */
export function KnowledgeBaseDocumentsPanel() {
  const { t, language } = useLanguage();
  const queryClient = useQueryClient();
  const list = useKnowledgeBaseDocuments();
  const documents = list.data ?? [];
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<KnowledgeBaseDocumentDto | null>(null);
  // There is one run at a time, whoever started it: this panel does not offer another while one is going, and shows how it is going only
  // when the run was started here (the panel above shows it for every run).
  const run = useReindexStatus();
  const reindex = run.data;
  const reindexRunning = reindex?.state === 'Running';
  const [watching, setWatching] = useState(false);

  const refresh = () => void queryClient.invalidateQueries({ queryKey: settingsKeys.documents });

  // When a run ends the list is loaded again: a document that was rebuilt may have another number of chunks.
  const wasRunning = useRef(false);
  useEffect(() => {
    if (wasRunning.current && !reindexRunning) void queryClient.invalidateQueries({ queryKey: settingsKeys.documents });
    wasRunning.current = reindexRunning;
  }, [reindexRunning, queryClient]);

  async function handleReindexDocument(d: KnowledgeBaseDocumentDto) {
    setError(null);
    const failure = await startReindex(queryClient, () => knowledgeBaseApi.reindexDocument(d.id), t('kbdoc_startFail'));
    setError(failure);
    if (!failure) setWatching(true);
  }

  async function handleFilesSelected(e: React.ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length === 0) return;
    setUploading(true);
    setError(null);
    try {
      await knowledgeBaseApi.uploadDocuments(files);
      refresh();
    } catch (err) {
      setError(messageOf(err, t('kbdoc_uploadFail')));
    } finally {
      setUploading(false);
    }
  }

  async function handleDeleteConfirmed() {
    if (!confirmDelete) return;
    const target = confirmDelete;
    setConfirmDelete(null);
    setError(null);
    try {
      await knowledgeBaseApi.deleteDocument(target.id);
      refresh();
    } catch (err) {
      setError(messageOf(err, t('kbdoc_removeFail')));
    }
  }

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">{t('kbdoc_title')}</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-600">{t('kbdoc_intro')}</p>
      <ErrorText>{error}</ErrorText>
      <label>
        <span className="sr-only">{t('kbdoc_uploadBtn')}</span>
        <input
          type="file"
          multiple
          accept=".docx,.xls,.xlsx,.pdf"
          onChange={handleFilesSelected}
          disabled={uploading}
          className="block w-full text-sm text-slate-600 file:mr-3 file:rounded-md file:border-0 file:bg-blue-700 file:px-3 file:py-2 file:text-sm file:font-medium file:text-white hover:file:bg-blue-800"
        />
      </label>
      {uploading && <p className="mt-2 text-xs text-slate-600">{t('kbdoc_uploading')}</p>}

      {list.isError && <ErrorText>{t('kbdoc_loadFail')}</ErrorText>}
      {list.isSuccess && documents.length > 0 && (
        <ul className="mt-4 space-y-1">
          {documents.map((d) => (
            <ListRow key={d.id}>
              <div className="min-w-0">
                <div className="truncate text-sm font-medium text-slate-800">{d.title}</div>
                <div className="text-xs text-slate-600">
                  {d.sourceFileName} · {t(d.chunkCount === 1 ? 'kbdoc_chunk' : 'kbdoc_chunks', { count: d.chunkCount })}
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-3">
                <button
                  className="text-xs text-blue-700 hover:underline disabled:opacity-50"
                  disabled={reindexRunning}
                  onClick={() => handleReindexDocument(d)}
                  title={reindexRunning ? t('kbdoc_reindexRunning') : t('kbdoc_rebuildTitle')}
                >
                  {t('kbdoc_reindex')}
                </button>
                <button className="text-xs text-red-600 hover:underline" onClick={() => setConfirmDelete(d)}>
                  {t('common_delete')}
                </button>
              </div>
            </ListRow>
          ))}
        </ul>
      )}
      {list.isSuccess && documents.length === 0 && <p className="mt-4 text-sm text-slate-600">{t('kbdoc_empty')}</p>}
      {watching && <ReindexStatusError run={run} />}
      {watching && reindex && reindex.state !== 'Idle' && (
        <p className={`mt-3 text-xs ${reindex.state === 'Failed' ? 'text-red-700' : reindex.state === 'Completed' ? 'text-green-700' : 'text-slate-600'}`}>
          {reindex.state === 'Running' && reindex.total > 0 ? `${reindex.processed} / ${reindex.total} · ` : ''}
          {reindex.message ? translateServerMessage(reindex.message, language) : null}
        </p>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('kbdoc_removeTitle')}
          message={t('kbdoc_removeMsg', { name: confirmDelete.title })}
          confirmLabel={t('common_delete')}
          onConfirm={handleDeleteConfirmed}
          onCancel={() => setConfirmDelete(null)}
        />
      )}
    </Panel>
  );
}
