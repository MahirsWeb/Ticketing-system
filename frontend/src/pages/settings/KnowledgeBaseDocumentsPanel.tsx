import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { knowledgeBaseApi, type ReindexStatus } from '../../api/knowledgeBase';
import { settingsKeys, useKnowledgeBaseDocuments } from '../../api/settingsQueries';
import { ErrorText } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { KnowledgeBaseDocumentDto } from '../../types';
import { messageOf } from './settingsHelpers';
import { ListRow, Panel } from './settingsUi';

function formatBytesAsChunks(chunkCount: number) {
  return `${chunkCount} ${chunkCount === 1 ? 'chunk' : 'chunks'}`;
}

/** The manuals and instructions the AI assistant answers from: uploaded, removed, and rebuilt from their stored originals. */
export function KnowledgeBaseDocumentsPanel() {
  const queryClient = useQueryClient();
  const list = useKnowledgeBaseDocuments();
  const documents = list.data ?? [];
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<KnowledgeBaseDocumentDto | null>(null);
  const [reindex, setReindex] = useState<ReindexStatus | null>(null);

  const refresh = () => void queryClient.invalidateQueries({ queryKey: settingsKeys.documents });

  // While a document is being rebuilt, follow its progress; when it ends, reload the list (the chunk count may have changed).
  const reindexRunning = reindex?.state === 'Running';
  useEffect(() => {
    if (!reindexRunning) return;
    const timer = window.setInterval(() => {
      knowledgeBaseApi.reindexStatus().then((s) => {
        setReindex(s);
        if (s.state !== 'Running') void queryClient.invalidateQueries({ queryKey: settingsKeys.documents });
      }).catch(() => undefined);
    }, 2000);
    return () => window.clearInterval(timer);
  }, [reindexRunning, queryClient]);

  async function handleReindexDocument(d: KnowledgeBaseDocumentDto) {
    setError(null);
    try {
      setReindex(await knowledgeBaseApi.reindexDocument(d.id));
    } catch (err: any) {
      if (err?.response?.status === 409 && err.response.data?.status) setReindex(err.response.data.status as ReindexStatus);
      else setError(messageOf(err, 'Could not start the re-index.'));
    }
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
      setError(messageOf(err, 'Could not upload one or more of the selected files.'));
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
      setError(messageOf(err, 'Could not remove this document.'));
    }
  }

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">Knowledge base documentation</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-500">
        Upload manuals or instructions (Word .docx, Excel .xls/.xlsx, PDF) so the AI assistant can answer
        questions using official documentation, not just past tickets. Legacy binary .doc isn't supported —
        re-save as .docx first.
      </p>
      <ErrorText>{error}</ErrorText>
      <label>
        <span className="sr-only">Upload documentation files</span>
        <input
          type="file"
          multiple
          accept=".docx,.xls,.xlsx,.pdf"
          onChange={handleFilesSelected}
          disabled={uploading}
          className="block w-full text-sm text-slate-600 file:mr-3 file:rounded-md file:border-0 file:bg-blue-700 file:px-3 file:py-2 file:text-sm file:font-medium file:text-white hover:file:bg-blue-800"
        />
      </label>
      {uploading && <p className="mt-2 text-xs text-slate-500">Uploading and indexing — this can take a moment per file…</p>}

      {list.isError && <ErrorText>The documentation could not be loaded. Try again in a moment.</ErrorText>}
      {list.isSuccess && documents.length > 0 && (
        <ul className="mt-4 space-y-1">
          {documents.map((d) => (
            <ListRow key={d.id}>
              <div className="min-w-0">
                <div className="truncate text-sm font-medium text-slate-800">{d.title}</div>
                <div className="text-xs text-slate-500">
                  {d.sourceFileName} · {formatBytesAsChunks(d.chunkCount)}
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-3">
                <button
                  className="text-xs text-blue-700 hover:underline disabled:opacity-50"
                  disabled={reindexRunning}
                  onClick={() => handleReindexDocument(d)}
                  title="Re-read the stored original and rebuild its searchable content"
                >
                  Re-index
                </button>
                <button className="text-xs text-red-600 hover:underline" onClick={() => setConfirmDelete(d)}>
                  Delete
                </button>
              </div>
            </ListRow>
          ))}
        </ul>
      )}
      {list.isSuccess && documents.length === 0 && <p className="mt-4 text-sm text-slate-500">No documentation uploaded yet.</p>}
      {reindex && reindex.state !== 'Idle' && (
        <p className={`mt-3 text-xs ${reindex.state === 'Failed' ? 'text-red-700' : reindex.state === 'Completed' ? 'text-green-700' : 'text-slate-600'}`}>
          {reindex.state === 'Running' && reindex.total > 0 ? `${reindex.processed} / ${reindex.total} · ` : ''}
          {reindex.message}
        </p>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Remove this document?"
          message={`"${confirmDelete.title}" and its indexed content will be permanently removed from the knowledge base. The AI assistant will no longer be able to reference it.`}
          confirmLabel="Delete"
          onConfirm={handleDeleteConfirmed}
          onCancel={() => setConfirmDelete(null)}
        />
      )}
    </Panel>
  );
}
