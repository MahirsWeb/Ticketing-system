import { format, parseISO } from 'date-fns';
import { Card } from '../../../components/ui';
import { useLanguage } from '../../../i18n/LanguageContext';
import { formatHours } from '../../../lib/hours';
import type { BranchLoadDto, CompanyCardDto } from '../../../types';
import { Bar, CardHeading } from './CompanyCards';

const day = (value: string | null) => (value ? format(parseISO(value), 'dd.MM.yyyy') : '—');

function Fact({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-md border border-slate-200 px-3 py-2">
      <div className="text-xs text-slate-600">{label}</div>
      <div className="text-lg font-bold tabular-nums text-slate-900">{value}</div>
    </div>
  );
}

/**
 * The company as a whole, over all the years: the first and the last ticket, the tickets by year (local years), the clients that are registered against those that
 * wrote (in the period and ever), and all the work recorded on its tickets. The period only counts the clients that wrote.
 */
export function CompanyCardCard({ data }: { data: CompanyCardDto }) {
  const { t } = useLanguage();
  const max = Math.max(0, ...data.byYear.map((y) => y.count));
  return (
    <Card className="p-5">
      <CardHeading title={t('co_card')} note={t('co_cardNote')} />
      {data.totalTickets === 0 ? (
        <p className="text-sm text-slate-600">{t('co_cardNone')}</p>
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <Fact label={t('co_cardFirst')} value={day(data.firstTicketAtUtc)} />
            <Fact label={t('co_cardLast')} value={day(data.lastTicketAtUtc)} />
            <Fact label={t('co_cardTotal')} value={data.totalTickets} />
            <Fact label={t('co_cardHours')} value={t('rp_hoursValue', { hours: formatHours(data.totalHours) })} />
          </div>
          <div className="mt-4 grid gap-6 md:grid-cols-2">
            <div>
              <h3 className="mb-1 text-xs font-semibold text-slate-700">{t('co_cardByYear')}</h3>
              <ul className="space-y-1">
                {data.byYear.map((y) => (
                  <li key={y.year} className="text-sm">
                    <div className="flex justify-between">
                      <span className="text-slate-800">{y.year}</span>
                      <span className="font-semibold tabular-nums text-slate-800">{y.count}</span>
                    </div>
                    <Bar value={y.count} max={max} />
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <h3 className="mb-1 text-xs font-semibold text-slate-700">{t('co_cardClients')}</h3>
              <dl className="space-y-1 text-sm">
                <div className="flex justify-between">
                  <dt className="text-slate-800">{t('co_cardRegistered')}</dt>
                  <dd className="font-semibold tabular-nums text-slate-800">{data.registeredClients}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-slate-800">{t('co_cardActivePeriod')}</dt>
                  <dd className="font-semibold tabular-nums text-slate-800">
                    {data.activeClientsInPeriod} / {data.registeredClients}
                  </dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-slate-800">{t('co_cardActiveEver')}</dt>
                  <dd className="font-semibold tabular-nums text-slate-800">
                    {data.activeClientsEver} / {data.registeredClients}
                  </dd>
                </div>
              </dl>
            </div>
          </div>
        </>
      )}
    </Card>
  );
}

const cell = 'px-2 py-1 text-right text-sm tabular-nums text-slate-800';
const head = 'px-2 py-1.5 text-right text-xs font-semibold text-slate-700';

/**
 * The branches that work for the company: the tickets created in the period, the ones closed in it, the hours of the staff of the branch on the company's
 * tickets (by the branch of the person, not of the ticket), and the share. The sub-branches of a branch are spelled out under it.
 */
export function BranchLoadCard({ data }: { data: BranchLoadDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5 lg:col-span-2">
      <CardHeading title={t('co_branchLoad')} note={t('co_branchLoadNote')} />
      {data.items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-200">
                <th scope="col" className="px-2 py-1.5 text-left text-xs font-semibold text-slate-700">{t('co_colBranch')}</th>
                <th scope="col" className={head}>{t('co_colTickets')}</th>
                <th scope="col" className={head}>{t('co_colClosed')}</th>
                <th scope="col" className={head}>{t('co_colHours')}</th>
                <th scope="col" className={head}>{t('co_colShare')}</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((b, index) => (
                <BranchRows key={b.departmentId ?? `none-${index}`} item={b} />
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="mt-3 text-xs text-slate-600">{t('co_branchTotal', { tickets: data.totalTickets, hours: formatHours(data.totalHours) })}</p>
    </Card>
  );
}

function BranchRows({ item }: { item: BranchLoadDto['items'][number] }) {
  const { t } = useLanguage();
  return (
    <>
      <tr className="border-b border-slate-100">
        <td className={`px-2 py-1 text-left text-sm ${item.departmentId === null ? 'italic text-slate-600' : 'font-medium text-slate-800'}`}>{item.departmentId === null ? t('co_notRouted') : item.name}</td>
        <td className={cell}>{item.tickets}</td>
        <td className={cell}>{item.closed}</td>
        <td className={cell}>{formatHours(item.hours)}</td>
        <td className={cell}>{t('em_percent', { percent: item.sharePercent })}</td>
      </tr>
      {item.subBranches.map((s, index) => (
        <tr key={s.subBranchId ?? `none-${index}`} className="border-b border-slate-100 text-xs">
          <td className={`py-1 pl-6 pr-2 text-left ${s.subBranchId === null ? 'italic text-slate-600' : 'text-slate-700'}`}>{s.subBranchId === null ? t('co_noSubBranch') : s.name}</td>
          <td className="px-2 py-1 text-right tabular-nums text-slate-700">{s.tickets}</td>
          <td className="px-2 py-1 text-right tabular-nums text-slate-700">{s.closed}</td>
          <td className="px-2 py-1 text-right tabular-nums text-slate-700">{formatHours(s.hours)}</td>
          <td className="px-2 py-1 text-right text-slate-600">—</td>
        </tr>
      ))}
    </>
  );
}
