import { useState } from 'react';
import { subDays } from 'date-fns';
import { useAttention, useFirstResponse, useResolutionTime, useTransfers } from '../../api/ticketInsightQueries';
import type { InactiveDays } from '../../api/ticketInsights';
import { useDepartments } from '../../api/lookupQueries';
import { DateRangePicker } from '../../components/DateRangePicker';
import { ErrorText, Select } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import { useAuthStore } from '../../store/authStore';
import { AgeCard, FirstResponseCard, ResolutionCard, TicketListCard, TransfersCard } from './TicketCards';

const DAY = 24 * 60 * 60 * 1000;
const CHOICES: InactiveDays[] = [2, 3, 7];

/**
 * The "Tickets" tab: what is old or stuck now (whatever the period), how long solving and the first reply take, and the transfers between branches (the
 * last three for the period). Staff see their own branch; Admin sees all of them and may choose one.
 */
export function TicketsReportTab() {
  const { t, language } = useLanguage();
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const [to, setTo] = useState(() => new Date());
  const [from, setFrom] = useState(() => subDays(new Date(), 30));
  const [departmentId, setDepartmentId] = useState('');
  const [inactiveDays, setInactiveDays] = useState<InactiveDays>(3);
  const { data: departments = [] } = useDepartments({ enabled: isAdmin });

  const branch = isAdmin && departmentId ? departmentId : undefined;
  const range = { from: from.toISOString(), to: to.toISOString(), departmentId: branch };
  const attention = useAttention({ departmentId: branch, inactiveDays });
  const resolution = useResolutionTime(range);
  const firstResponse = useFirstResponse(range);
  const transfers = useTransfers(range);
  const failed = [attention, resolution, firstResponse, transfers].some((q) => q.isError);

  const asOf = attention.data ? new Date(attention.data.asOfUtc).getTime() : Date.now();
  const idleDays = (last: string | null) => (last ? Math.max(0, Math.floor((asOf - new Date(last).getTime()) / DAY)) : 0);
  const date = (value: string | null) => (value ? new Date(value).toLocaleDateString(language === 'bs' ? 'bs-BA' : 'en-GB') : '');

  return (
    <div className="space-y-6">
      <div className="sticky top-0 z-20 -mx-4 flex flex-wrap items-center justify-between gap-3 border-b border-slate-300 bg-slate-200 px-4 py-3">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{t('rp_title')}</h1>
          <p className="text-xs text-slate-600">{isAdmin ? t('rp_scopeAdmin') : t('rp_scopeStaff')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {isAdmin && (
            <div className="w-48">
              <Select aria-label={t('filter_branchAria')} value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
                <option value="">{t('filter_allBranches')}</option>
                {departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </Select>
            </div>
          )}
          <DateRangePicker
            from={from}
            to={to}
            onChange={(f, tt) => {
              setFrom(f ?? subDays(new Date(), 30));
              setTo(tt ?? new Date());
            }}
          />
        </div>
      </div>

      <ErrorText>{failed ? t('dash_partialFail') : null}</ErrorText>

      <section aria-label={t('rp_nowSection')} className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="text-base font-bold text-slate-900">{t('rp_nowSection')}</h2>
            <p className="text-xs text-slate-600">{t('rp_nowNote')}</p>
          </div>
          <label className="flex items-center gap-2 text-sm text-slate-700">
            {t('rp_inactiveAria')}
            <Select className="w-28" value={inactiveDays} onChange={(e) => setInactiveDays(Number(e.target.value) as InactiveDays)}>
              {CHOICES.map((d) => (
                <option key={d} value={d}>
                  {t('rp_daysOption', { count: d })}
                </option>
              ))}
            </Select>
          </label>
        </div>
        {attention.data && (
          <div className="grid gap-4 lg:grid-cols-2">
            <AgeCard data={attention.data} />
            <TicketListCard
              title={t('rp_inactive')}
              note={t('rp_noteInactive', { days: attention.data.inactiveDays })}
              list={attention.data.inactive}
              empty={t('rp_noInactive')}
              detail={(i) => t('rp_idleFor', { days: idleDays(i.lastActivityUtc) })}
            />
            <TicketListCard
              title={t('rp_unassigned')}
              note={t('rp_noteUnassigned')}
              list={attention.data.unassigned}
              empty={t('rp_noUnassigned')}
              detail={(i) => t('rp_ageOf', { days: i.ageDays })}
            />
            <TicketListCard
              title={t('rp_dueSoon')}
              note={t('rp_noteDueSoon')}
              list={attention.data.dueSoon}
              empty={t('rp_noDueSoon')}
              detail={(i) => t('rp_dueAt', { date: date(i.dueDateUtc) })}
            />
            <TicketListCard
              title={t('rp_overdue')}
              note={t('rp_noteOverdue')}
              list={attention.data.overdue}
              empty={t('rp_noOverdue')}
              detail={(i) => t('rp_dueAt', { date: date(i.dueDateUtc) })}
            />
          </div>
        )}
      </section>

      <section aria-label={t('rp_periodSection')} className="space-y-4">
        <div>
          <h2 className="text-base font-bold text-slate-900">{t('rp_periodSection')}</h2>
          <p className="text-xs text-slate-600">{t('rp_periodNote')}</p>
        </div>
        <div className="grid gap-4 lg:grid-cols-2">
          {resolution.data && <ResolutionCard data={resolution.data} />}
          {firstResponse.data && <FirstResponseCard data={firstResponse.data} />}
        </div>
        {transfers.data && <TransfersCard data={transfers.data} />}
      </section>
    </div>
  );
}
