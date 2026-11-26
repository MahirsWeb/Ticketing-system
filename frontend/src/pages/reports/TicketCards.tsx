import { format, parseISO } from 'date-fns';
import { Link } from 'react-router-dom';
import { Card, StatusBadge } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';
import type { AgeGroupDto, AttentionDto, FirstResponseDto, ResolutionTimeDto, TicketBriefDto, TicketBriefListDto, TransfersDto } from '../../types';
import { Bar, CardHeading } from '../dashboard/companies/CompanyCards';

const AGE_LABEL: Record<string, TranslationKey> = {
  upTo2Days: 'co_age2',
  upTo7Days: 'co_age7',
  upTo30Days: 'co_age30',
  older: 'co_ageOlder',
};

function TicketLink({ ticket }: { ticket: Pick<TicketBriefDto, 'id' | 'ticketNumber' | 'title'> }) {
  return (
    <Link to={`/tickets/${ticket.id}`} className="min-w-0 font-medium text-blue-700 hover:underline">
      <span className="font-semibold">{ticket.ticketNumber}</span> <span className="break-words text-slate-800">{ticket.title}</span>
    </Link>
  );
}

/** Active tickets by how long ago they were created, each group with its oldest ticket (a link to it). */
export function AgeCard({ data }: { data: AttentionDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('rp_age')} note={t('rp_noteAge')} />
      {data.activeTotal === 0 ? (
        <p className="text-sm text-slate-600">{t('rp_nothingActive')}</p>
      ) : (
        <>
          <ul className="space-y-2">
            {data.ages.map((g: AgeGroupDto) => (
              <li key={g.key} className="rounded-md bg-slate-50 px-3 py-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-slate-800">{t(AGE_LABEL[g.key] ?? 'co_ageOlder')}</span>
                  <span className="font-semibold text-slate-800">{g.count}</span>
                </div>
                <Bar value={g.count} max={data.activeTotal} />
                {g.oldest && (
                  <p className="mt-1 flex flex-wrap items-baseline gap-x-1 text-xs text-slate-600">
                    {t('rp_oldest', { days: g.oldest.ageDays })}
                    <TicketLink ticket={g.oldest} />
                  </p>
                )}
              </li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-slate-600">{t('rp_activeTotal', { count: data.activeTotal })}</p>
        </>
      )}
    </Card>
  );
}

/** A list of tickets that need attention. It shows the first ones and says how many there are in all. */
export function TicketListCard({
  title,
  note,
  list,
  empty,
  detail,
}: {
  title: string;
  note: string;
  list: TicketBriefListDto;
  empty: string;
  detail: (ticket: TicketBriefDto) => string;
}) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={title} note={note} />
      {list.total === 0 ? (
        <p className="text-sm text-slate-600">{empty}</p>
      ) : (
        <>
          <div className="mb-2 text-2xl font-bold text-slate-900">{list.total}</div>
          <ul className="space-y-2">
            {list.items.map((i) => (
              <li key={i.id} className="flex items-start justify-between gap-3 rounded-md bg-slate-50 px-3 py-2 text-sm">
                <span className="min-w-0">
                  <TicketLink ticket={i} />
                  <span className="ml-2 inline-block align-middle">
                    <StatusBadge status={i.status} />
                  </span>
                </span>
                <span className="shrink-0 text-xs text-slate-600">{detail(i)}</span>
              </li>
            ))}
          </ul>
          {list.total > list.items.length && <p className="mt-3 text-xs text-slate-600">{t('rp_showing', { shown: list.items.length, total: list.total })}</p>}
        </>
      )}
    </Card>
  );
}

function Hours({ label, value }: { label: string; value: number }) {
  const { t } = useLanguage();
  return (
    <div className="rounded-md bg-slate-50 px-3 py-2">
      <div className="text-xs text-slate-600">{label}</div>
      <div className="text-xl font-bold text-slate-900">{t('rp_hoursValue', { hours: value })}</div>
    </div>
  );
}

/** Average, median and 90th percentile of the time to solve: the average alone hides the exceptions. */
export function ResolutionCard({ data }: { data: ResolutionTimeDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('rp_resolution')} note={t('rp_noteResolution')} />
      {data.count === 0 ? (
        <p className="text-sm text-slate-600">{t('rp_noClosed')}</p>
      ) : (
        <>
          <div className="grid grid-cols-3 gap-2">
            <Hours label={t('rp_average')} value={data.averageHours} />
            <Hours label={t('rp_median')} value={data.medianHours} />
            <Hours label={t('rp_p90')} value={data.p90Hours} />
          </div>
          <p className="mt-3 text-xs text-slate-600">{t('rp_basedOn', { count: data.count })}</p>
        </>
      )}
    </Card>
  );
}

/** The time from the report to the first reply of staff. A ticket nobody has answered is counted apart. */
export function FirstResponseCard({ data }: { data: FirstResponseDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('rp_firstResponse')} note={t('rp_noteFirstResponse')} />
      {data.tickets === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <>
          {data.answered > 0 ? (
            <div className="grid grid-cols-3 gap-2">
              <Hours label={t('rp_average')} value={data.averageHours} />
              <Hours label={t('rp_median')} value={data.medianHours} />
              <Hours label={t('rp_p90')} value={data.p90Hours} />
            </div>
          ) : (
            <p className="text-sm text-slate-600">{t('rp_noReplies')}</p>
          )}
          <p className="mt-3 text-xs text-slate-600">{t('rp_answeredLine', { answered: data.answered, tickets: data.tickets, unanswered: data.unanswered })}</p>
        </>
      )}
    </Card>
  );
}

/** Transfers between branches: from which to which, and the tickets. Many between the same two branches is a sign of bad routing. */
export function TransfersCard({ data }: { data: TransfersDto }) {
  const { t } = useLanguage();
  const name = (value: string | null) => value ?? t('rp_unrouted');
  const max = data.edges[0]?.count ?? 0;
  return (
    <Card className="p-5">
      <CardHeading title={t('rp_transfers')} note={t('rp_noteTransfers')} />
      {data.total === 0 ? (
        <p className="text-sm text-slate-600">{t('rp_noTransfers')}</p>
      ) : (
        <>
          <ul className="space-y-2">
            {data.edges.map((e) => (
              <li key={`${e.fromId ?? '-'}>${e.toId}`} className="rounded-md bg-slate-50 px-3 py-2 text-sm">
                <div className="flex justify-between gap-3">
                  <span className="text-slate-800">
                    {name(e.fromName)} → {e.toName}
                  </span>
                  <span className="shrink-0 font-semibold text-slate-800">{t('co_times', { count: e.count })}</span>
                </div>
                <Bar value={e.count} max={max} />
              </li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-slate-600">{t('rp_transferTotal', { count: data.total })}</p>
          <h3 className="mb-1 mt-4 text-xs font-semibold text-slate-700">{t('rp_transferTickets')}</h3>
          <ul className="space-y-1">
            {data.items.map((i) => (
              <li key={`${i.ticketId}-${i.atUtc}`} className="flex flex-wrap items-baseline justify-between gap-x-3 text-sm">
                <TicketLink ticket={{ id: i.ticketId, ticketNumber: i.ticketNumber, title: i.title }} />
                <span className="text-xs text-slate-600">
                  {name(i.fromName)} → {i.toName} · {format(parseISO(i.atUtc), 'dd.MM.yyyy HH:mm')}
                </span>
              </li>
            ))}
          </ul>
          {data.total > data.items.length && <p className="mt-2 text-xs text-slate-600">{t('rp_showing', { shown: data.items.length, total: data.total })}</p>}
        </>
      )}
    </Card>
  );
}
