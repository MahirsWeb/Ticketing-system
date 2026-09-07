import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { knowledgeBaseApi } from '../../api/knowledgeBase';
import type { SimilarTicketsResponseDto } from '../../types';
import { Card, Spinner, StatusBadge } from '../../components/ui';

export default function SimilarTicketsPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const query = searchParams.get('query') ?? '';
  const [data, setData] = useState<SimilarTicketsResponseDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!query) {
      setLoading(false);
      return;
    }
    setLoading(true);
    knowledgeBaseApi.similarTickets(query).then((res) => {
      setData(res);
      setLoading(false);
    });
  }, [query]);

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <button onClick={() => navigate(-1)} className="mb-2 text-sm text-blue-700 hover:underline">
          ← Back
        </button>
        <h1 className="text-xl font-bold text-slate-900">Similar problems report</h1>
        <p className="mt-1 text-sm text-slate-500">
          Every closed ticket whose internal documentation looks similar to: <span className="font-medium text-slate-700">“{query}”</span>
        </p>
      </div>

      {loading && (
        <div className="flex items-center gap-2 py-8 text-sm text-slate-500">
          <Spinner className="h-4 w-4" /> Analyzing the knowledge base…
        </div>
      )}

      {!loading && data && (
        <>
          <div className="grid grid-cols-3 gap-4">
            <Card className="p-5 text-center">
              <div className="text-3xl font-bold text-slate-900">{data.similarTicketCount}</div>
              <div className="mt-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Similar tickets</div>
            </Card>
            <Card className="p-5 text-center">
              <div className="text-3xl font-bold text-slate-900">{data.totalEligibleTicketCount}</div>
              <div className="mt-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Total documented tickets</div>
            </Card>
            <Card className="p-5 text-center">
              <div className="text-3xl font-bold text-blue-700">{data.similarPercentage}%</div>
              <div className="mt-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Share of the whole base</div>
            </Card>
          </div>

          <Card className="overflow-hidden">
            <div className="border-b border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
              Matching tickets
            </div>
            {data.tickets.length === 0 ? (
              <p className="p-5 text-sm text-slate-400">No similar tickets found.</p>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 text-left text-xs font-semibold uppercase text-slate-500">
                    <th className="px-4 py-2">#</th>
                    <th className="px-4 py-2">Title</th>
                    <th className="px-4 py-2">Status</th>
                    <th className="px-4 py-2">Closed</th>
                    <th className="px-4 py-2">Match</th>
                  </tr>
                </thead>
                <tbody>
                  {data.tickets.map((t) => (
                    <tr
                      key={t.ticketId}
                      className="cursor-pointer border-b border-slate-100 hover:bg-slate-50"
                      onClick={() => window.open(`/tickets/${t.ticketId}`, '_blank')}
                    >
                      <td className="px-4 py-2 font-medium text-blue-700">#{t.ticketNumber}</td>
                      <td className="px-4 py-2">{t.title}</td>
                      <td className="px-4 py-2">{t.status && <StatusBadge status={t.status} />}</td>
                      <td className="px-4 py-2 text-slate-500">
                        {t.closedAtUtc ? new Date(t.closedAtUtc).toLocaleDateString() : '—'}
                      </td>
                      <td className="px-4 py-2 text-slate-500">{t.matchScore}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>
        </>
      )}
    </div>
  );
}
