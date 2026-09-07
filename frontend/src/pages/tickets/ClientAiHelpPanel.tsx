import { useRef, useState } from 'react';
import { knowledgeBaseApi } from '../../api/knowledgeBase';
import type { KnowledgeBaseSearchResultDto } from '../../types';
import { Button, Card, Input, Spinner } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { useLanguage } from '../../i18n/LanguageContext';

/// Client-facing AI help panel. Deliberately separate from the staff KnowledgeBaseSearchPanel: different
/// backend endpoint (never sees ticket internal notes — see /api/knowledge-base/ask-client), different
/// tone, and no ticket-number/source exposure beyond official help documentation titles.
export function ClientAiHelpPanel({ initialQuery = '' }: { initialQuery?: string }) {
  const { t } = useLanguage();
  const [query, setQuery] = useState(initialQuery);
  const [answer, setAnswer] = useState<string | null>(null);
  const [sources, setSources] = useState<KnowledgeBaseSearchResultDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [open, setOpen] = useState(false);
  const [asked, setAsked] = useState(false);
  const [confirmingStop, setConfirmingStop] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  async function handleAsk(e?: React.FormEvent) {
    e?.preventDefault();
    if (!query.trim()) return;
    const controller = new AbortController();
    abortRef.current = controller;
    setLoading(true);
    setAsked(true);
    try {
      const res = await knowledgeBaseApi.askClient(query, controller.signal);
      setAnswer(res.answer);
      setSources(res.sources);
    } catch (err: any) {
      if (err?.code !== 'ERR_CANCELED') throw err;
    } finally {
      setLoading(false);
      abortRef.current = null;
    }
  }

  function confirmStop() {
    abortRef.current?.abort();
    setConfirmingStop(false);
    setLoading(false);
  }

  if (!open) {
    return (
      <Button variant="secondary" onClick={() => setOpen(true)}>
        🤖 {t('aiHelp_title')}
      </Button>
    );
  }

  return (
    <Card className="p-5">
      {confirmingStop && (
        <ConfirmDialog
          title={t('aiHelp_stopConfirmTitle')}
          message={t('aiHelp_stopConfirmBody')}
          confirmLabel={t('aiHelp_stop')}
          danger
          onConfirm={confirmStop}
          onCancel={() => setConfirmingStop(false)}
        />
      )}
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-bold uppercase tracking-wide text-slate-500">{t('aiHelp_panelTitle')}</h2>
        <button className="text-xs text-slate-400 hover:underline" onClick={() => setOpen(false)}>
          {t('aiHelp_hide')}
        </button>
      </div>
      <p className="mb-3 text-xs text-slate-400">{t('aiHelp_disclaimer')}</p>
      <form onSubmit={handleAsk} className="mb-3 flex gap-2">
        <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('aiHelp_placeholder')} />
        {loading ? (
          <Button type="button" variant="danger" onClick={() => setConfirmingStop(true)}>
            {t('aiHelp_stop')}
          </Button>
        ) : (
          <Button type="submit">{t('aiHelp_ask')}</Button>
        )}
      </form>

      {loading && (
        <div className="flex items-center gap-2 py-4 text-sm text-slate-500">
          <Spinner className="h-4 w-4" /> {t('aiHelp_searching')}
        </div>
      )}

      {!loading && asked && answer && (
        <div className="mb-4 rounded-md border border-blue-100 bg-blue-50 p-4 text-sm text-slate-800">
          <p className="whitespace-pre-wrap">{answer}</p>
        </div>
      )}

      {!loading && sources.length > 0 && (
        <>
          <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">{t('aiHelp_sourcesUsed')}</div>
          <ul className="space-y-2">
            {sources.map((r) => (
              <li key={r.documentId} className="rounded-md border border-slate-200 p-3 text-sm">
                {r.documentFileUrl ? (
                  <a href={r.documentFileUrl} target="_blank" rel="noreferrer" className="font-medium text-blue-700 hover:underline">
                    📄 {r.title}
                  </a>
                ) : (
                  <span className="font-medium text-slate-700">📄 {r.title}</span>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </Card>
  );
}
