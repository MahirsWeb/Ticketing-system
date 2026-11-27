import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { format, parseISO } from 'date-fns';
import { documentsApi } from '../../api/documents';
import { formatHours } from '../../lib/hours';
import { useTicketCard } from '../../api/documentQueries';
import { Button, Card, ErrorText, Spinner, StatusBadge } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';
import type { StatusSpanDto, TicketCardDto, TicketCardEventDto } from '../../types';

const stamp = (value: string | null) => (value ? format(parseISO(value), 'dd.MM.yyyy HH:mm') : '—');

/** Seconds as hours and minutes ("2 h 05 min"): short stretches are not rounded to nothing. */
function span(seconds: number, t: ReturnType<typeof useLanguage>['t']) {
  const minutes = Math.round(seconds / 60);
  return t('tk_duration', { hours: Math.floor(minutes / 60), minutes: String(minutes % 60).padStart(2, '0') });
}

function EventLine({ e }: { e: TicketCardEventDto }) {
  const { t } = useLanguage();
  const status = (s: string | null) => (s ? t(`status_${s}` as TranslationKey) : '');
  let detail = '';
  if (e.kind === 'StatusChanged') detail = `${status(e.from)} → ${status(e.to)}`;
  else if (e.kind === 'Transferred') detail = `${e.from ?? t('rp_unrouted')} → ${e.to}`;
  else if (e.kind === 'WorkSession') detail = span(e.durationSeconds ?? 0, t) + (e.running ? ` (${t('tk_running')})` : '');
  return (
    <li className="grid grid-cols-[9.5rem_1fr] gap-3 border-b border-slate-100 py-2 text-sm last:border-0 print:break-inside-avoid">
      <time className="text-slate-600" dateTime={e.atUtc}>
        {stamp(e.atUtc)}
      </time>
      <div>
        <span className="font-medium text-slate-900">{t(`tk_ev_${e.kind}` as TranslationKey)}</span>
        {e.person && <span className="text-slate-700"> · {e.person}</span>}
        {detail && <span className="text-slate-700"> · {detail}</span>}
      </div>
    </li>
  );
}

function SpanRow({ s }: { s: StatusSpanDto }) {
  const { t } = useLanguage();
  return (
    <tr className="border-b border-slate-100 last:border-0">
      <td className="px-2 py-1.5 text-sm text-slate-800">{s.status ? t(`status_${s.status}` as TranslationKey) : <span className="italic">{t('tk_unknown')}</span>}</td>
      <td className="px-2 py-1.5 text-sm text-slate-700">{stamp(s.fromUtc)}</td>
      <td className="px-2 py-1.5 text-sm text-slate-700">{s.toUtc ? stamp(s.toUtc) : t('tk_stillGoing')}</td>
      <td className="px-2 py-1.5 text-right text-sm tabular-nums text-slate-800">{span(s.seconds, t)}</td>
    </tr>
  );
}

function Fact({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{label}</div>
      <div className="text-sm text-slate-800">{value ?? '—'}</div>
    </div>
  );
}

function Header({ card }: { card: TicketCardDto }) {
  const { t } = useLanguage();
  const h = card.header;
  return (
    <Card className="p-5">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <Fact label={t('tk_company')} value={h.company} />
        <Fact label={t('tk_client')} value={h.client} />
        <Fact label={t('detail_branch')} value={h.branch} />
        <Fact label={t('tk_assignee')} value={h.assignee} />
        <Fact label={t('tk_created')} value={stamp(h.createdAt)} />
        <Fact label={t('detail_openedAt')} value={stamp(h.openedAtUtc)} />
        <Fact label={t('detail_closedAt')} value={stamp(h.closedAtUtc)} />
        <Fact label={t('tk_resolution')} value={h.resolutionHours === null ? null : t('rp_hoursValue', { hours: formatHours(h.resolutionHours) })} />
        <Fact label={t('detail_dueDate')} value={stamp(h.dueDateUtc)} />
        <Fact label={t('tk_sla')} value={h.slaMet === null ? null : h.slaMet ? t('tk_yes') : t('tk_no')} />
        <Fact label={t('tk_staffReplies')} value={h.staffReplies} />
        <Fact label={t('tk_clientReplies')} value={h.clientReplies} />
        <Fact label={t('tk_internalNotes')} value={h.internalNotes} />
        <Fact label={t('tk_workHours')} value={t('rp_hoursValue', { hours: formatHours(h.workHours) })} />
      </div>
    </Card>
  );
}

/**
 * The card of a ticket: its whole life on one line of time, for printing ("Save as PDF" in the browser) and as an xlsx. The text of the messages is not on it, only
 * that they were written and when. A client sees a reduced card. What the trail of the ticket does not say is shown as unknown and not guessed.
 */
export default function TicketCardPage() {
  const { t } = useLanguage();
  const { id } = useParams<{ id: string }>();
  const { data: card, isPending, error } = useTicketCard(id);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  async function download() {
    if (!id) return;
    setBusy(true);
    setFailed(false);
    try {
      await documentsApi.ticketCardXlsx(id);
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  if (isPending) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }
  if (error || !card) {
    return <ErrorText>{(error as { response?: { data?: { message?: string } } } | null)?.response?.data?.message ?? t('detail_loadError')}</ErrorText>;
  }

  const h = card.header;
  const total = card.statusTotals.reduce((sum, s) => sum + s.seconds, 0) + card.unknownSeconds;
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3 print:hidden">
        <Link to={`/tickets/${h.id}`} className="text-sm font-medium text-blue-700 hover:underline">
          ← {t('tk_back')}
        </Link>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => window.print()}>
            {t('tk_print')}
          </Button>
          <Button variant="secondary" onClick={download} disabled={busy}>
            {busy ? t('dash_exporting') : <><span aria-hidden>⬇</span> {t('em_download')}</>}
          </Button>
        </div>
      </div>
      <ErrorText>{failed ? t('em_downloadFailed') : null}</ErrorText>

      <div>
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-600">{t('tk_title')}</p>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-xl font-bold text-slate-900">
            #{h.ticketNumber} — {h.title}
          </h1>
          <StatusBadge status={h.status} />
        </div>
        <p className="mt-1 text-xs text-slate-600">{t('tk_asOf', { time: stamp(card.asOfUtc) })}</p>
      </div>

      <Header card={card} />

      <Card className="p-5">
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">{t('tk_timeline')}</h2>
        <p className="mb-2 text-xs text-slate-600">{t('tk_noteTimeline')}</p>
        <ol>
          {card.events.map((e, i) => (
            <EventLine key={`${e.atUtc}-${e.kind}-${i}`} e={e} />
          ))}
        </ol>
      </Card>

      <Card className="p-5 print:break-inside-avoid">
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">{t('tk_inStatus')}</h2>
        <p className="mb-2 text-xs text-slate-600">{t('tk_noteStatus')}</p>
        <table className="w-full">
          <thead>
            <tr className="border-b border-slate-200">
              <th scope="col" className="px-2 py-1.5 text-left text-xs font-semibold text-slate-700">{t('tk_status')}</th>
              <th scope="col" className="px-2 py-1.5 text-left text-xs font-semibold text-slate-700">{t('tk_from')}</th>
              <th scope="col" className="px-2 py-1.5 text-left text-xs font-semibold text-slate-700">{t('tk_to')}</th>
              <th scope="col" className="px-2 py-1.5 text-right text-xs font-semibold text-slate-700">{t('tk_time')}</th>
            </tr>
          </thead>
          <tbody>
            {card.spans.map((s, i) => (
              <SpanRow key={`${s.fromUtc}-${i}`} s={s} />
            ))}
          </tbody>
        </table>
        <ul className="mt-3 space-y-1 text-sm text-slate-800">
          {card.statusTotals.map((s) => (
            <li key={s.status} className="flex justify-between">
              <span>{t(`status_${s.status}` as TranslationKey)}</span>
              <span className="tabular-nums">{span(s.seconds, t)}</span>
            </li>
          ))}
          {card.unknownSeconds > 0 && (
            <li className="flex justify-between italic">
              <span>{t('tk_unknown')}</span>
              <span className="tabular-nums">{span(card.unknownSeconds, t)}</span>
            </li>
          )}
        </ul>
        {!card.trailComplete && <p className="mt-3 text-xs text-amber-800">{t('tk_trailIncomplete')}</p>}
        <p className="mt-2 text-xs text-slate-600">{t('tk_totalAge', { time: span(total, t) })}</p>
      </Card>
    </div>
  );
}
