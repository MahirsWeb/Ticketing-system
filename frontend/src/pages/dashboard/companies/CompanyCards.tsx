import { Card } from '../../../components/ui';
import { TopFiveList } from '../TopFiveList';
import { useLanguage } from '../../../i18n/LanguageContext';
import type { BranchCountDto, BreakdownItemDto, ClosedByDto, CompanyClientStatDto, CompanyRowDto, TopCompaniesDto } from '../../../types';
import { Change } from './Change';

/** The title of a card with, under it, which tickets it counts: the ones created in the period or the ones closed in it. */
export function CardHeading({ title, cohort }: { title: string; cohort: 'created' | 'closed' }) {
  const { t } = useLanguage();
  return (
    <div className="mb-4">
      <h2 className="text-sm font-bold uppercase tracking-wide text-slate-600">{title}</h2>
      <p className="text-xs text-slate-600">{cohort === 'created' ? t('co_cohortCreated') : t('co_cohortClosed')}</p>
    </div>
  );
}

function Bar({ value, max }: { value: number; max: number }) {
  const width = max > 0 ? Math.max(2, Math.round((value / max) * 100)) : 0;
  return (
    <div className="h-1.5 w-full rounded bg-slate-100" aria-hidden>
      <div className="h-1.5 rounded bg-blue-600" style={{ width: `${width}%` }} />
    </div>
  );
}

/** The companies that sent the most tickets, with what part of all of them each makes. A click on a name chooses the company. */
export function TopCompaniesCard({ data, onPick, seeMore }: { data: TopCompaniesDto; onPick: (company: CompanyRowDto) => void; seeMore: React.ReactNode }) {
  const { t } = useLanguage();
  const max = data.items[0]?.total ?? 0;
  return (
    <Card className="p-5">
      <CardHeading title={t('co_topCompanies')} cohort="created" />
      {data.items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <>
          <ol className="space-y-3">
            {data.items.map((c, i) => (
              <li key={c.companyId} className="text-sm">
                <div className="flex items-center justify-between gap-3">
                  <span className="min-w-0 truncate">
                    <span className="mr-2 font-bold text-slate-600">#{i + 1}</span>
                    <button type="button" className="font-medium text-blue-700 hover:underline" onClick={() => onPick(c)}>
                      {c.name}
                    </button>
                  </span>
                  <span className="flex shrink-0 items-center gap-3">
                    <Change total={c.total} previous={c.previousTotal} />
                    <span className="w-36 whitespace-nowrap text-right text-xs text-slate-600">{t('co_ticketsShare', { count: c.total, share: c.sharePercent })}</span>
                  </span>
                </div>
                <Bar value={c.total} max={max} />
              </li>
            ))}
          </ol>
          <p className="mt-4 text-xs text-slate-600">{t('co_topTogether', { top: data.topTickets, total: data.totalTickets, companies: data.totalCompanies })}</p>
        </>
      )}
      <div className="mt-3">{seeMore}</div>
    </Card>
  );
}

/** A breakdown (topics or categories): a row for each, and a row for the tickets that have none yet, so that the rows add up to the total. */
export function BreakdownCard({ title, items }: { title: string; items: BreakdownItemDto[] }) {
  const { t } = useLanguage();
  const max = items.reduce((m, i) => Math.max(m, i.count), 0);
  return (
    <Card className="p-5">
      <CardHeading title={title} cohort="created" />
      {items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <TopFiveList
          items={items}
          keyOf={(i, idx) => i.id ?? `none-${idx}`}
          render={(i) => (
            <div className="rounded-md bg-slate-50 px-3 py-2 text-sm">
              <div className="flex items-center justify-between gap-3">
                <span className={i.id == null ? 'italic text-slate-600' : 'font-medium text-slate-800'}>
                  {i.name ?? t('co_none')}
                  {i.id != null && !i.isActive && <span className="ml-2 text-xs font-normal text-slate-600">({t('co_inactive')})</span>}
                </span>
                <span className="font-semibold text-slate-800">{i.count}</span>
              </div>
              <Bar value={i.count} max={max} />
            </div>
          )}
        />
      )}
    </Card>
  );
}

/** The branches the tickets went to, and under each the sub-branches. */
export function BranchesCard({ items }: { items: BranchCountDto[] }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('co_byBranch')} cohort="created" />
      {items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <TopFiveList
          items={items}
          keyOf={(b, idx) => b.departmentId ?? `none-${idx}`}
          render={(b) => (
            <div className="rounded-md bg-slate-50 px-3 py-2 text-sm">
              <div className="flex items-center justify-between">
                <span className={b.departmentId == null ? 'italic text-slate-600' : 'font-medium text-slate-800'}>{b.name ?? t('co_none')}</span>
                <span className="font-semibold text-slate-800">{b.count}</span>
              </div>
              {(b.subBranches.length > 0 || (b.departmentId != null && b.noSubBranchCount > 0 && b.noSubBranchCount !== b.count)) && (
                <ul className="mt-1 space-y-0.5 border-l border-slate-300 pl-3 text-xs text-slate-600">
                  {b.subBranches.map((s) => (
                    <li key={s.subBranchId} className="flex justify-between">
                      <span>{s.name}</span>
                      <span>{s.count}</span>
                    </li>
                  ))}
                  {b.noSubBranchCount > 0 && (
                    <li className="flex justify-between italic">
                      <span>{t('co_noSubBranch')}</span>
                      <span>{b.noSubBranchCount}</span>
                    </li>
                  )}
                </ul>
              )}
            </div>
          )}
        />
      )}
    </Card>
  );
}

/** Who closed the tickets that were closed in the period, and in which branches those were. */
export function ClosedByCard({ data }: { data: ClosedByDto }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('co_closedBy')} cohort="closed" />
      {data.people.length === 0 ? (
        <p className="text-sm text-slate-600">{t('dash_noClosed')}</p>
      ) : (
        <>
          <TopFiveList
            items={data.people}
            keyOf={(p) => p.userId}
            render={(p, i) => (
              <div className="rounded-md bg-slate-50 px-3 py-2 text-sm">
                <div className="flex items-center justify-between">
                  <span>
                    <span className="mr-2 font-bold text-slate-600">#{i + 1}</span>
                    {p.name}
                  </span>
                  <span className="font-semibold text-slate-800">{t('dash_countClosed', { count: p.closedCount })}</span>
                </div>
                {p.branches.length > 0 && (
                  <p className="mt-1 text-xs text-slate-600">{p.branches.map((b) => `${b.name ?? t('co_none')}: ${b.count}`).join(' · ')}</p>
                )}
              </div>
            )}
          />
          <p className="mt-3 text-xs text-slate-600">{t('co_closedTotal', { count: data.totalClosed })}</p>
        </>
      )}
    </Card>
  );
}

/**
 * The people of one company who reported the most. A click on a name chooses the person, if the person can be chosen (`canPick`): somebody whose account has
 * been switched off is not in the list of the box, and choosing them would filter the figures with the box looking empty.
 */
export function ClientsCard({ items, onPick, canPick }: { items: CompanyClientStatDto[]; onPick: (clientId: string) => void; canPick: (clientId: string) => boolean }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <CardHeading title={t('co_clients')} cohort="created" />
      {items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('co_noTickets')}</p>
      ) : (
        <TopFiveList
          items={items}
          keyOf={(c) => c.clientId}
          render={(c) => (
            <div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
              {canPick(c.clientId) ? (
                <button type="button" className="font-medium text-blue-700 hover:underline" onClick={() => onPick(c.clientId)}>
                  {c.name}
                </button>
              ) : (
                <span className="font-medium text-slate-800">{c.name}</span>
              )}
              <span className="flex gap-3 text-xs text-slate-600">
                <span>{t('co_countTotal', { count: c.total })}</span>
                <span className="text-amber-700">{t('dash_countOpen', { count: c.open })}</span>
                <span className="text-green-700">{t('dash_countClosed', { count: c.closed })}</span>
              </span>
            </div>
          )}
        />
      )}
    </Card>
  );
}
