import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useSimilarTickets } from '../../api/knowledgeBaseQueries';
import { messageOf } from '../../lib/messageOf';
import { Button, Card, ErrorText, Spinner, StatusBadge } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

/** Where "Back" goes when the page was opened directly (a bookmark, a link) and there is nothing in this tab to go back to. */
const FALLBACK = '/ai-assistant';

export default function SimilarTicketsPage() {
  const { t } = useLanguage();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const query = searchParams.get('query') ?? '';
  const noText = query.trim() === '';
  const report = useSimilarTickets(query);
  const data = report.data;
  // The first entry of a tab has the key "default": nothing before it belongs to this application.
  const goBack = () => (location.key === 'default' ? navigate(FALLBACK) : navigate(-1));

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <button onClick={goBack} className="mb-2 text-sm text-blue-700 hover:underline">
          {t('sim_back')}
        </button>
        <h1 className="text-xl font-bold text-slate-900">{t('sim_title')}</h1>
        <p className="mt-1 text-sm text-slate-600">
          {t('sim_subtitle')} <span className="font-medium text-slate-700">“{query}”</span>
        </p>
      </div>

      {noText && <p className="text-sm text-slate-600">{t('sim_noText')}</p>}

      {report.isLoading && (
        <div role="status" className="flex items-center gap-2 py-8 text-sm text-slate-600">
          <Spinner className="h-4 w-4" /> {t('sim_analyzing')}
        </div>
      )}

      {report.isError && (
        <div>
          <ErrorText>{messageOf(report.error, t('sim_reportFail'))}</ErrorText>
          <Button variant="secondary" className="mt-3" onClick={() => void report.refetch()}>
            {t('common_tryAgain')}
          </Button>
        </div>
      )}

      {data && (
        <>
          <div className="grid grid-cols-3 gap-4">
            <Card className="p-5 text-center">
              <div className="text-3xl font-bold text-slate-900">{data.similarTicketCount}</div>
              <div className="mt-1 text-xs font-semibold uppercase tracking-wide text-slate-600">{t('sim_similarTickets')}</div>
            </Card>
            <Card className="p-5 text-center">
              <div className="text-3xl font-bold text-slate-900">{data.totalEligibleTicketCount}</div>
              <div className="mt-1 text-xs font-semibold uppercase tracking-wide text-slate-600">{t('sim_totalDocumented')}</div>
            </Card>
            <Card className="p-5 text-center">
              <div className="text-3xl font-bold text-blue-700">{data.similarPercentage}%</div>
              <div className="mt-1 text-xs font-semibold uppercase tracking-wide text-slate-600">{t('sim_share')}</div>
            </Card>
          </div>

          <Card className="overflow-hidden">
            <div className="border-b border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold uppercase tracking-wide text-slate-600">
              {t('sim_matchingTickets')}
            </div>
            {data.tickets.length === 0 ? (
              <p className="p-5 text-sm text-slate-600">{t('sim_noMatches')}</p>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 text-left text-xs font-semibold uppercase text-slate-600">
                    <th className="px-4 py-2">#</th>
                    <th className="px-4 py-2">{t('table_title')}</th>
                    <th className="px-4 py-2">{t('col_status')}</th>
                    <th className="px-4 py-2">{t('col_closed')}</th>
                    <th className="px-4 py-2">{t('col_match')}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.tickets.map((t) => (
                    <tr
                      key={t.ticketId}
                      className="cursor-pointer border-b border-slate-100 hover:bg-slate-50"
                      onClick={() => window.open(`/tickets/${t.ticketId}`, '_blank', 'noopener,noreferrer')}
                    >
                      <td className="px-4 py-2 font-medium">
                        {/* The row can be clicked, but only a link can be reached and followed with the keyboard. */}
                        <a
                          href={`/tickets/${t.ticketId}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          onClick={(e) => e.stopPropagation()}
                          className="text-blue-700 hover:underline"
                        >
                          #{t.ticketNumber}
                        </a>
                      </td>
                      <td className="px-4 py-2">{t.title}</td>
                      <td className="px-4 py-2">{t.status && <StatusBadge status={t.status} />}</td>
                      <td className="px-4 py-2 text-slate-600">
                        {t.closedAtUtc ? new Date(t.closedAtUtc).toLocaleDateString() : '—'}
                      </td>
                      <td className="px-4 py-2 text-slate-600">{t.matchScore}%</td>
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
