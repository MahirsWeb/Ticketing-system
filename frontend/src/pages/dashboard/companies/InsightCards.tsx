import { format, parseISO } from 'date-fns';
import { Card } from '../../../components/ui';
import { useLanguage } from '../../../i18n/LanguageContext';
import type { ConversationDto, OpenAgeDto, QuietCompaniesDto, RepeatedTopicsDto, RhythmDto, TimeSpentDto } from '../../../types';
import { Bar, CardHeading } from './CompanyCards';

/** The tickets that are not closed now, by how long ago they were created, and how old the oldest is. Whatever the period. */
export function OpenAgeCard({ data }: { data: OpenAgeDto }) {
  const { t } = useLanguage();
  const rows = [
    { label: t('co_age2'), count: data.upTo2Days },
    { label: t('co_age7'), count: data.upTo7Days },
    { label: t('co_age30'), count: data.upTo30Days },
    { label: t('co_ageOlder'), count: data.older },
  ];
  return (
    <Card className="p-5">
      <CardHeading title={t('co_openAge')} note={t('co_noteOpen')} />
      {data.total === 0 ? (
        <p className="text-sm text-slate-600">{t('co_nothingOpen')}</p>
      ) : (
        <>
          <ul className="space-y-2">
            {rows.map((r) => (
              <li key={r.label} className="rounded-md bg-slate-50 px-3 py-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-slate-800">{r.label}</span>
                  <span className="font-semibold text-slate-800">{r.count}</span>
                </div>
                <Bar value={r.count} max={data.total} />
              </li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-slate-600">{t('co_openNow', { count: data.total })}</p>
          {data.oldestCompany && <p className="text-xs text-slate-600">{t('co_oldest', { days: data.oldestDays, company: data.oldestCompany })}</p>}
        </>
      )}
    </Card>
  );
}

/** The time written down on tickets (work sessions that started in the period). */
export function TimeSpentCard({ data }: { data: TimeSpentDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('co_timeSpent')} note={t('co_noteTime')} />
      {data.sessions === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTime')}</p>
      ) : (
        <>
          <div className="text-2xl font-bold text-slate-900">{t('co_hoursTotal', { hours: data.totalHours })}</div>
          <p className="mt-1 text-sm text-slate-700">{t('co_sessionsLine', { sessions: data.sessions, tickets: data.tickets })}</p>
          <p className="text-sm text-slate-700">{t('co_avgPerTicket', { hours: data.avgHoursPerTicket })}</p>
        </>
      )}
      {data.runningSessions > 0 && <p className="mt-2 text-xs text-slate-600">{t('co_runningSessions', { count: data.runningSessions })}</p>}
    </Card>
  );
}

/** Replies per ticket, and how long a ticket waited to be picked up. */
export function ConversationCard({ data }: { data: ConversationDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('co_conversation')} note={t('co_noteConversation')} />
      {data.tickets === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <>
          <div className="text-2xl font-bold text-slate-900">{t('co_repliesPerTicket', { value: data.avgMessagesPerTicket })}</div>
          <p className="mt-2 text-sm text-slate-700">{data.pickedUp > 0 ? t('co_avgPickup', { hours: data.avgHoursToPickup }) : t('co_noPickup')}</p>
          <p className="text-xs text-slate-600">{t('co_pickedUpLine', { picked: data.pickedUp, total: data.tickets, not: data.notPickedUp })}</p>
        </>
      )}
    </Card>
  );
}

function Columns({ values, label }: { values: number[]; label: (index: number) => string }) {
  const max = Math.max(1, ...values);
  return (
    <div className="flex items-end gap-1" role="img" aria-label={values.map((v, i) => `${label(i)}: ${v}`).join(', ')}>
      {values.map((v, i) => (
        <div key={i} className="flex min-w-0 flex-1 flex-col items-center" title={`${label(i)}: ${v}`}>
          <span className="text-[10px] text-slate-600">{v > 0 ? v : ''}</span>
          <div className="w-full rounded-t bg-blue-600" style={{ height: `${Math.round((v / max) * 64)}px`, minHeight: v > 0 ? 2 : 0 }} aria-hidden />
          <span className="text-[10px] text-slate-600" aria-hidden>
            {label(i)}
          </span>
        </div>
      ))}
    </div>
  );
}

/** When tickets arrive: by weekday, and by hour of the day in the business time zone. */
export function RhythmCard({ data }: { data: RhythmDto }) {
  const { t } = useLanguage();
  const weekdays = [t('co_wd0'), t('co_wd1'), t('co_wd2'), t('co_wd3'), t('co_wd4'), t('co_wd5'), t('co_wd6')];
  return (
    <Card className="p-5">
      <CardHeading title={t('co_rhythm')} note={t('co_noteRhythm', { zone: data.timeZone })} />
      {data.total === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <div className="space-y-4">
          <div>
            <h3 className="mb-1 text-xs font-semibold text-slate-700">{t('co_byWeekday')}</h3>
            <Columns values={data.byWeekday} label={(i) => weekdays[i]} />
          </div>
          <div>
            <h3 className="mb-1 text-xs font-semibold text-slate-700">{t('co_byHour')}</h3>
            <Columns values={data.byHour} label={(i) => String(i)} />
          </div>
        </div>
      )}
    </Card>
  );
}

/** The same person reporting the same topic more than once: something that is not getting solved for good. */
export function RepeatedCard({ data }: { data: RepeatedTopicsDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('co_repeated')} note={t('co_noteRepeated')} />
      {data.groups === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noRepeated')}</p>
      ) : (
        <>
          <ul className="space-y-2">
            {data.items.slice(0, 5).map((i) => (
              <li key={`${i.clientId}-${i.topicId}`} className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
                <span>
                  <span className="font-medium text-slate-800">{i.clientName}</span>
                  <span className="text-slate-600"> · {i.topicName}</span>
                </span>
                <span className="font-semibold text-slate-800">{t('co_times', { count: i.count })}</span>
              </li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-slate-600">{t('co_repeatedSummary', { groups: data.groups, tickets: data.tickets })}</p>
        </>
      )}
    </Card>
  );
}

/** Companies that sent tickets in the period right before and none in this one. */
export function QuietCard({ data }: { data: QuietCompaniesDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('co_quietCompanies')} note={t('co_noteQuiet')} />
      {data.total === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noQuiet')}</p>
      ) : (
        <>
          <ul className="space-y-2">
            {data.items.slice(0, 5).map((c) => (
              <li key={c.companyId} className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
                <span className="font-medium text-slate-800">{c.name}</span>
                <span className="text-xs text-slate-600">
                  {t('co_before', { previous: c.previousTotal })}
                  {c.lastTicketAtUtc && ` · ${t('co_lastTicket', { date: format(parseISO(c.lastTicketAtUtc), 'dd.MM.yyyy') })}`}
                </span>
              </li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-slate-600">{t('co_quietTotal', { count: data.total })}</p>
        </>
      )}
    </Card>
  );
}
