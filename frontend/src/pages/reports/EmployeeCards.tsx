import { useState } from 'react';
import { format, parseISO } from 'date-fns';
import { employeeReportsApi } from '../../api/employeeReports';
import { Button, Card, ErrorText } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import type { DependencyDto, HoursDetailDto, HoursPeopleDto, SpecializationDto, SpecializationItemDto, WorkloadDto } from '../../types';
import { CardHeading } from '../dashboard/companies/CompanyCards';

const th = 'px-2 py-1.5 text-left text-xs font-semibold text-slate-700';
const td = 'px-2 py-1.5 text-sm text-slate-800';
const num = 'px-2 py-1.5 text-right text-sm tabular-nums text-slate-800';

/** Open tickets and tasks of each person. Tasks have no due date, so "overdue" is only about tickets. */
export function WorkloadCard({ data }: { data: WorkloadDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('em_load')} note={t('em_noteLoad')} />
      {data.people.length === 0 ? (
        <p className="text-sm text-slate-600">{t('em_noPeople')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-200">
                <th scope="col" className={th}>{t('em_colPerson')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colTickets')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colMain')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colOverdue')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colTasks')}</th>
              </tr>
            </thead>
            <tbody>
              {data.people.map((p) => (
                <tr key={p.userId} className="border-b border-slate-100 last:border-0">
                  <td className={td}>
                    {p.name}
                    {p.branch && <span className="ml-2 text-xs text-slate-600">{p.branch}</span>}
                  </td>
                  <td className={num}>{p.openTickets}</td>
                  <td className={num}>{p.mainTickets}</td>
                  <td className={`${num} ${p.overdueTickets > 0 ? 'font-semibold text-red-700' : ''}`}>{p.overdueTickets}</td>
                  <td className={num}>{p.openTasks}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

/** The recorded hours of each person in the period; a click on a name chooses the person for the details below. */
export function HoursCard({ data, selectedId, onPick }: { data: HoursPeopleDto; selectedId: string | null; onPick?: (userId: string) => void }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('em_hours')} note={t('em_noteHours')} />
      {data.people.length === 0 ? (
        <p className="text-sm text-slate-600">{t('em_noPeople')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-200">
                <th scope="col" className={th}>{t('em_colPerson')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colTicketHours')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colTaskHours')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colHours')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colNoEntry')}</th>
              </tr>
            </thead>
            <tbody>
              {data.people.map((p) => (
                <tr key={p.userId} className={`border-b border-slate-100 last:border-0 ${p.userId === selectedId ? 'bg-blue-50' : ''}`}>
                  <td className={td}>
                    {onPick ? (
                      <button type="button" className="font-medium text-blue-700 hover:underline" aria-pressed={p.userId === selectedId} onClick={() => onPick(p.userId)}>
                        {p.name}
                      </button>
                    ) : (
                      p.name
                    )}
                  </td>
                  <td className={num}>{p.ticketHours}</td>
                  <td className={num}>{p.taskHours}</td>
                  <td className={`${num} font-semibold`}>{p.totalHours}</td>
                  <td className={num}>{p.daysWithoutEntry ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

const day = (value: string) => format(parseISO(value), 'dd.MM.yyyy');

/** One person's hours by week and by day, and the working days with nothing recorded. */
export function HoursDetailCard({ data }: { data: HoursDetailDto }) {
  const { t } = useLanguage();
  const worked = data.days.filter((d) => d.totalHours > 0);
  return (
    <Card className="p-5">
      <CardHeading title={t('em_detailFor', { name: data.name })} note={t('em_noteDetail')} />
      <p className="mb-3 text-sm text-slate-800">{t('em_detailTotal', { total: data.totalHours, tickets: data.ticketHours, tasks: data.taskHours })}</p>
      <div className="grid gap-6 lg:grid-cols-2">
        <div>
          <h3 className="mb-1 text-xs font-semibold text-slate-700">{t('em_byWeek')}</h3>
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-200">
                <th scope="col" className={th}>{t('em_week')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colTicketHours')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colTaskHours')}</th>
                <th scope="col" className={`${th} text-right`}>{t('em_colHours')}</th>
              </tr>
            </thead>
            <tbody>
              {data.weeks.map((w) => (
                <tr key={w.weekStart} className="border-b border-slate-100 last:border-0">
                  <td className={td}>{t('em_weekOf', { date: day(w.weekStart) })}</td>
                  <td className={num}>{w.ticketHours}</td>
                  <td className={num}>{w.taskHours}</td>
                  <td className={`${num} font-semibold`}>{w.totalHours}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div>
          <h3 className="mb-1 text-xs font-semibold text-slate-700">{t('em_byDay')}</h3>
          {worked.length === 0 ? (
            <p className="text-sm text-slate-600">{t('em_noHours')}</p>
          ) : (
            <table className="w-full">
              <thead>
                <tr className="border-b border-slate-200">
                  <th scope="col" className={th}>{t('em_day')}</th>
                  <th scope="col" className={`${th} text-right`}>{t('em_colTicketHours')}</th>
                  <th scope="col" className={`${th} text-right`}>{t('em_colTaskHours')}</th>
                  <th scope="col" className={`${th} text-right`}>{t('em_colHours')}</th>
                </tr>
              </thead>
              <tbody>
                {worked.map((d) => (
                  <tr key={d.day} className="border-b border-slate-100 last:border-0">
                    <td className={td}>{day(d.day)}</td>
                    <td className={num}>{d.ticketHours}</td>
                    <td className={num}>{d.taskHours}</td>
                    <td className={`${num} font-semibold`}>{d.totalHours}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
      <div className="mt-4 border-t border-slate-100 pt-3 text-sm text-slate-800">
        {!data.daysWithoutEntryApply ? (
          <p className="text-xs text-slate-600">{t('em_noEntryNotApplicable')}</p>
        ) : data.daysWithoutEntry.length === 0 ? (
          <p>{t('em_noDaysWithout')}</p>
        ) : (
          <>
            <p className="font-medium">{t('em_daysWithout', { count: data.daysWithoutEntry.length })}</p>
            <p className="mt-1 text-xs text-slate-600">{data.daysWithoutEntry.map(day).join(', ')}</p>
          </>
        )}
      </div>
    </Card>
  );
}

function Top({ title, items, empty }: { title: string; items: SpecializationItemDto[]; empty: string }) {
  const { t } = useLanguage();
  return (
    <div>
      <h3 className="mb-1 text-xs font-semibold text-slate-700">{title}</h3>
      {items.length === 0 ? (
        <p className="text-sm text-slate-600">{empty}</p>
      ) : (
        <ol className="space-y-1">
          {items.map((i, index) => (
            <li key={i.id} className="flex items-baseline justify-between gap-2 rounded-md bg-slate-50 px-3 py-2 text-sm">
              <span className="text-slate-800">
                {index + 1}. {i.name}
              </span>
              <span className="shrink-0 text-xs text-slate-600">{t('em_closedHours', { closed: i.closed, hours: i.hours })}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

/** Where a person works the most: the top three topics, categories and companies. */
export function SpecializationCard({ data }: { data: SpecializationDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('em_specialization', { name: data.name })} note={t('em_noteSpecialization')} />
      <p className="mb-3 text-sm text-slate-800">{t('em_closedTotal', { count: data.closedTotal })}</p>
      <div className="grid gap-4 lg:grid-cols-3">
        <Top title={t('em_topics')} items={data.topics} empty={t('em_nothingHere')} />
        <Top title={t('em_categories')} items={data.categories} empty={t('em_nothingHere')} />
        <Top title={t('em_companies')} items={data.companies} empty={t('em_nothingHere')} />
      </div>
    </Card>
  );
}

/** Companies where one person closed most of the tickets of the last 12 months: what is lost when that person is away. */
export function DependencyCard({ data }: { data: DependencyDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('em_dependency')} note={t('em_noteDependency', { closed: data.minClosed, share: data.minSharePercent })} />
      {data.items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('em_noDependency')}</p>
      ) : (
        <>
          <ul className="space-y-2">
            {data.items.map((i) => (
              <li key={i.companyId} className="flex items-baseline justify-between gap-3 rounded-md bg-slate-50 px-3 py-2 text-sm">
                <span className="min-w-0">
                  <span className="font-medium text-slate-800">{i.company}</span>
                  <span className="ml-2 text-xs text-slate-600">{t('em_dependencyLine', { person: i.person, closed: i.personClosed, total: i.closed })}</span>
                </span>
                <span className="shrink-0 font-semibold text-slate-800">
                  {t('em_percent', { percent: i.sharePercent })}
                  {i.high && <span className="ml-2 rounded bg-red-100 px-1.5 py-0.5 text-xs font-semibold text-red-800">{t('em_high')}</span>}
                </span>
              </li>
            ))}
          </ul>
          {data.total > data.items.length && <p className="mt-3 text-xs text-slate-600">{t('rp_showing', { shown: data.items.length, total: data.total })}</p>}
        </>
      )}
    </Card>
  );
}

/** The monthly worksheet of a person, downloaded as xlsx. */
export function TimesheetCard({ userId }: { userId: string | undefined }) {
  const { t } = useLanguage();
  const [month, setMonth] = useState(() => format(new Date(), 'yyyy-MM'));
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  async function download() {
    if (!userId || !month) return;
    const [year, m] = month.split('-').map(Number);
    setBusy(true);
    setFailed(false);
    try {
      await employeeReportsApi.timesheet({ userId, year, month: m });
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="p-5">
      <CardHeading title={t('em_timesheet')} note={t('em_noteTimesheet')} />
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm text-slate-700">
          {t('em_month')}
          <input type="month" value={month} onChange={(e) => setMonth(e.target.value)} className="rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm text-slate-900" />
        </label>
        <Button variant="secondary" onClick={download} disabled={!userId || !month || busy}>
          {busy ? t('dash_exporting') : <><span aria-hidden>⬇</span> {t('em_download')}</>}
        </Button>
      </div>
      <ErrorText>{failed ? t('em_downloadFailed') : null}</ErrorText>
    </Card>
  );
}
