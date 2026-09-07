import { useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { knowledgeBaseApi } from '../../api/knowledgeBase';
import type { KnowledgeBaseSearchResultDto } from '../../types';
import { Button, Card, Input, Spinner, StatusBadge } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';

const SOURCES_PREVIEW_COUNT = 7;

function SourceRow({ r }: { r: KnowledgeBaseSearchResultDto }) {
  const body = (
    <>
      <div className="flex items-center gap-2 text-sm font-medium text-blue-700">
        {r.sourceType === 'Ticket' ? (
          <>
            #{r.ticketNumber} — {r.title}
            {r.status && <StatusBadge status={r.status} />}
          </>
        ) : (
          <>📄 {r.title}</>
        )}
      </div>
      {r.resolutionSummary && <p className="mt-1 line-clamp-2 text-xs text-slate-500">{r.resolutionSummary}</p>}
      {r.sourceType === 'Document' && <p className="mt-1 text-xs text-slate-400">Uploaded documentation</p>}
    </>
  );

  const href = r.sourceType === 'Ticket' ? `/tickets/${r.ticketId}` : r.documentFileUrl ?? undefined;

  if (!href) return <div className="block rounded-md border border-slate-200 p-3">{body}</div>;
  return (
    <a href={href} target="_blank" rel="noreferrer" className="block rounded-md border border-slate-200 p-3 hover:bg-slate-50">
      {body}
    </a>
  );
}

export function KnowledgeBaseSearchPanel({ initialQuery = '', alwaysOpen = false }: { initialQuery?: string; alwaysOpen?: boolean }) {
  const navigate = useNavigate();
  const [query, setQuery] = useState(initialQuery);
  const [askedQuery, setAskedQuery] = useState('');
  const [answer, setAnswer] = useState<string | null>(null);
  const [sources, setSources] = useState<KnowledgeBaseSearchResultDto[]>([]);
  const [similarCount, setSimilarCount] = useState(0);
  const [totalEligible, setTotalEligible] = useState(0);
  const [similarPct, setSimilarPct] = useState(0);
  const [loading, setLoading] = useState(false);
  const [open, setOpen] = useState(alwaysOpen);
  const [asked, setAsked] = useState(false);
  const [showAllSources, setShowAllSources] = useState(false);
  const [confirmingStop, setConfirmingStop] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  async function handleAsk(e?: React.FormEvent) {
    e?.preventDefault();
    if (!query.trim()) return;
    const controller = new AbortController();
    abortRef.current = controller;
    setLoading(true);
    setAsked(true);
    setAskedQuery(query);
    setShowAllSources(false);
    try {
      const res = await knowledgeBaseApi.ask(query, controller.signal);
      setAnswer(res.answer);
      setSources(res.sources);
      setSimilarCount(res.similarTicketCount);
      setTotalEligible(res.totalEligibleTicketCount);
      setSimilarPct(res.similarPercentage);
    } catch (err: any) {
      if (err?.code !== 'ERR_CANCELED') throw err;
    } finally {
      setLoading(false);
      abortRef.current = null;
    }
  }

  function requestStop() {
    setConfirmingStop(true);
  }

  function confirmStop() {
    abortRef.current?.abort();
    setConfirmingStop(false);
    setLoading(false);
  }

  if (!open) {
    return (
      <Button variant="secondary" onClick={() => setOpen(true)}>
        🤖 Ask the AI assistant about this ticket
      </Button>
    );
  }

  return (
    <Card className="p-5">
      {confirmingStop && (
        <ConfirmDialog
          title="Stop the AI request?"
          message="This will cancel the search that's currently in progress."
          confirmLabel="Stop"
          danger
          onConfirm={confirmStop}
          onCancel={() => setConfirmingStop(false)}
        />
      )}
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-bold uppercase tracking-wide text-slate-500">AI Assistant — Knowledge Base</h2>
        {!alwaysOpen && (
          <button className="text-xs text-slate-400 hover:underline" onClick={() => setOpen(false)}>
            Hide
          </button>
        )}
      </div>
      <p className="mb-3 text-xs text-slate-400">
        Answers are generated only from your organization's own closed tickets and uploaded documentation — never
        from general internet knowledge.
      </p>
      <form onSubmit={handleAsk} className="mb-3 flex gap-2">
        <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Describe the problem in a few keywords…" />
        {loading ? (
          <Button type="button" variant="danger" onClick={requestStop}>
            Stop
          </Button>
        ) : (
          <Button type="submit">Ask AI</Button>
        )}
      </form>

      {loading && (
        <div className="flex items-center gap-2 py-4 text-sm text-slate-500">
          <Spinner className="h-4 w-4" /> Searching the knowledge base and asking the AI…
        </div>
      )}

      {!loading && asked && answer && (
        <div className="mb-4 rounded-md border border-blue-100 bg-blue-50 p-4 text-sm text-slate-800">
          <div className="mb-1 text-xs font-semibold uppercase tracking-wide text-blue-700">AI answer</div>
          <p className="whitespace-pre-wrap">{answer}</p>
          <p className="mt-3 border-t border-blue-100 pt-3 text-xs text-red-600">
            <span className="font-bold">WARNING!</span> This answer is suggested based on tickets and documentation in
            the knowledge base — it doesn't mean it's 100% relevant to use for this specific type of problem. Consult
            with a more experienced colleague before relying on it.
          </p>
        </div>
      )}

      {!loading && asked && totalEligible > 0 && (
        <div className="mb-4 flex items-center justify-between rounded-md border border-slate-200 bg-slate-50 px-4 py-3">
          <div className="text-sm text-slate-700">
            <span className="font-bold text-slate-900">
              {similarCount} of {totalEligible} tickets ({similarPct}%)
            </span>{' '}
            look similar to this problem.
          </div>
          {similarCount > 0 && (
            <button
              className="text-xs font-medium text-blue-700 hover:underline"
              onClick={() => navigate(`/knowledge-base/similar?query=${encodeURIComponent(askedQuery)}`)}
            >
              View the list &amp; stats →
            </button>
          )}
        </div>
      )}

      {!loading && sources.length > 0 && (
        <>
          <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Sources used as context</div>
          <ul className="space-y-2">
            {(showAllSources ? sources : sources.slice(0, SOURCES_PREVIEW_COUNT)).map((r) => (
              <li key={`${r.sourceType}-${r.ticketId ?? r.documentId}`}>
                <SourceRow r={r} />
              </li>
            ))}
          </ul>
          {!showAllSources && sources.length > SOURCES_PREVIEW_COUNT && (
            <button
              className="mt-2 text-xs font-medium text-blue-700 hover:underline"
              onClick={() => setShowAllSources(true)}
            >
              See more ({sources.length - SOURCES_PREVIEW_COUNT} more)
            </button>
          )}
        </>
      )}

      {!loading && asked && sources.length === 0 && !answer && (
        <p className="text-sm text-slate-400">No similar tickets or documentation found yet.</p>
      )}
    </Card>
  );
}
