import { format, parseISO } from 'date-fns';
import { Link } from 'react-router-dom';
import { StatusBadge } from '../../components/ui';
import { formatHours } from '../../lib/hours';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';
import type { CompanyMonthlyDto, NamedCountDto } from '../../types';

const stamp = (value: string | null) => (value ? format(parseISO(value), 'dd.MM.yyyy HH:mm') : '—');

function Figure({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-md border border-slate-200 px-3 py-2">
      <div className="text-xs text-slate-600">{label}</div>
      <div className="text-xl font-bold tabular-nums text-slate-900">{value}</div>
    </div>
  );
}

function Counts({ title, rows, none }: { title: string; rows: { key: string; label: string; count: number }[]; none?: string }) {
  void none;
  return (
    <div className="print:break-inside-avoid">
      <h3 className="mb-1 text-xs font-semibold text-slate-700">{title}</h3>
      <table className="w-full">
        <tbody>
          {rows.map((r) => (
            <tr key={r.key} className="border-b border-slate-100 last:border-0">
              <td className="px-2 py-1 text-sm text-slate-800">{r.label}</td>
              <td className="px-2 py-1 text-right text-sm tabular-nums text-slate-800">{r.count}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * The monthly report of a company as a document: what is on the screen is what the xlsx has. The version for the client has no internal category, no hours unless
 * they were asked for, and no names of staff (not even of who made it). An employee's list holds only the tickets of their branch, and says so.
 */
export function CompanyMonthlyDocument({ data }: { data: CompanyMonthlyDto }) {
  const { t } = useLanguage();
  const h = data.header;
  const isClient = h.audience === 'client';
  const named = (items: NamedCountDto[]) => items.map((i) => ({ key: i.id ?? 'none', label: i.id === null ? t('mr_none') : i.name, count: i.count }));
  return (
    <article className="space-y-5 rounded-lg border border-slate-200 bg-white p-6 print:border-0 print:p-0" aria-label={t('mr_title')}>
      <header>
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('mr_title')}</p>
        <h2 className="text-xl font-bold text-slate-900">{h.company}</h2>
        <p className="text-sm text-slate-800">
          {t('mr_month', { month: `${String(h.month).padStart(2, '0')}.${h.year}` })} · {isClient ? t('mr_versionClient') : t('mr_versionInternal')}
        </p>
        <p className="mt-1 text-xs text-slate-600">{t('mr_madeBy', { by: h.madeBy, time: stamp(h.madeAtUtc) })}</p>
        <p className="text-xs text-slate-600">{t('mr_snapshot')}</p>
        <p className="text-xs text-slate-600">{h.listScope === 'all' ? t('mr_scopeAll') : t('mr_scopeBranch', { branch: h.listScope })}</p>
        {isClient && <p className="text-xs text-slate-600">{t('mr_clientNote')}</p>}
      </header>

      <section aria-label={t('mr_figures')} className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Figure label={t('mr_created')} value={data.created} />
        <Figure label={t('mr_closed')} value={data.closed} />
        <Figure label={t('rp_average')} value={data.resolutionCount === 0 ? '—' : t('rp_hoursValue', { hours: formatHours(data.averageResolutionHours) })} />
        <Figure label={t('rp_median')} value={data.resolutionCount === 0 ? '—' : t('rp_hoursValue', { hours: formatHours(data.medianResolutionHours) })} />
        <Figure label={t('mr_sla')} value={data.slaComplianceRate === null ? t('mr_noSla') : t('em_percent', { percent: data.slaComplianceRate })} />
        {data.workHours !== null && <Figure label={t('mr_workHours')} value={t('rp_hoursValue', { hours: formatHours(data.workHours) })} />}
      </section>
      <p className="text-xs text-slate-600">{t('mr_noteFigures', { count: data.resolutionCount, sla: data.slaCounted })}</p>

      <section aria-label={t('mr_distribution')} className="grid gap-5 md:grid-cols-3">
        <Counts title={t('mr_byStatus')} rows={data.byStatus.map((s) => ({ key: s.status, label: t(`status_${s.status}` as TranslationKey), count: s.count }))} />
        <Counts title={t('mr_byTopic')} rows={named(data.byTopic)} />
        {data.byCategory && <Counts title={t('mr_byCategory')} rows={named(data.byCategory)} />}
      </section>

      <section aria-label={t('mr_tickets')}>
        <h3 className="mb-1 text-xs font-semibold text-slate-700">{t('mr_tickets')}</h3>
        {data.tickets.length === 0 ? (
          <p className="text-sm text-slate-600">{t('mr_noTickets')}</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-slate-200">
                  {(['tk_ticket', 'mr_ticketTitle', 'tk_status', 'tk_created', 'detail_closedAt', 'tk_resolution'] as const).map((key) => (
                    <th key={key} scope="col" className="px-2 py-1.5 text-left text-xs font-semibold text-slate-700">
                      {t(key)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.tickets.map((x) => (
                  <tr key={x.id} className="border-b border-slate-100 last:border-0 print:break-inside-avoid">
                    <td className="px-2 py-1.5 text-sm font-medium text-slate-800">
                      {isClient ? x.ticketNumber : (
                        <Link to={`/tickets/${x.id}`} className="text-blue-700 hover:underline">
                          {x.ticketNumber}
                        </Link>
                      )}
                    </td>
                    <td className="px-2 py-1.5 text-sm text-slate-800">{x.title}</td>
                    <td className="px-2 py-1.5 text-sm">
                      <StatusBadge status={x.status} />
                    </td>
                    <td className="px-2 py-1.5 text-sm text-slate-700">{stamp(x.createdAt)}</td>
                    <td className="px-2 py-1.5 text-sm text-slate-700">{stamp(x.closedAtUtc)}</td>
                    <td className="px-2 py-1.5 text-right text-sm tabular-nums text-slate-800">{x.resolutionHours === null ? '—' : formatHours(x.resolutionHours)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data.ticketsTruncated && (
          <p role="note" className="mt-2 rounded-md bg-amber-50 px-3 py-2 text-xs font-medium text-amber-900">
            {t('mr_truncated', { shown: data.tickets.length, total: data.ticketsTotal })}
          </p>
        )}
      </section>
    </article>
  );
}
