import { useEffect, useState } from 'react';
import { format } from 'date-fns';
import { useBranchGantt, useGantt } from '../../api/reportQueries';
import { Button, Card, ErrorText, Select } from '../../components/ui';
import { GanttChart } from '../../components/GanttChart';
import { PersonOptions } from './PersonOptions';
import type { EmployeeGroups } from './employeeGroups';
import { useLanguage } from '../../i18n/LanguageContext';

/**
 * One person's day (tickets and tasks on the same hour axis), or everybody's in a branch for an administrator. Staff see their own day at
 * once; an administrator chooses somebody. Only the answer for the person, branch and day that are chosen now is shown.
 */
export function DailyTimeline({
  isAdmin,
  userId,
  departments,
  people,
}: {
  isAdmin: boolean;
  userId: string | undefined;
  departments: { id: string; name: string }[];
  people: EmployeeGroups;
}) {
  const { t } = useLanguage();
  const [mode, setMode] = useState<'person' | 'branch'>('person');
  const [personId, setPersonId] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [date, setDate] = useState(() => new Date());

  // Staff default to their own timeline; Admin must pick someone.
  useEffect(() => {
    if (!isAdmin && userId) setPersonId(userId);
  }, [isAdmin, userId]);

  const day = format(date, 'yyyy-MM-dd');
  const personTimeline = useGantt(personId, day, mode === 'person');
  const branchTimeline = useBranchGantt(departmentId, day, mode === 'branch');
  const timeline = mode === 'branch' ? branchTimeline : personTimeline;
  const loading = timeline.isLoading;
  const error = timeline.isError ? ((timeline.error as { response?: { data?: { message?: string } } }).response?.data?.message ?? t('dash_timelineError')) : null;
  const branchData = branchTimeline.data ?? null;

  return (
    <Card className="p-5">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-sm font-bold uppercase tracking-wide text-slate-600">{t('dash_dailyTimeline')}</h2>
          <p className="text-xs text-slate-600">{t('dash_timelineIntro')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {isAdmin && (
            <div className="flex rounded-md border border-slate-300 p-0.5 text-xs">
              <button
                className={`rounded px-2.5 py-1 font-medium ${mode === 'person' ? 'bg-blue-700 text-white' : 'text-slate-600'}`}
                onClick={() => setMode('person')}
              >
                {t('dash_modePerson')}
              </button>
              <button
                className={`rounded px-2.5 py-1 font-medium ${mode === 'branch' ? 'bg-blue-700 text-white' : 'text-slate-600'}`}
                onClick={() => setMode('branch')}
              >
                {t('dash_modeBranch')}
              </button>
            </div>
          )}
          {mode === 'person' ? (
            <div className="w-72">
              <Select aria-label={t('dash_personAria')} value={personId} onChange={(e) => setPersonId(e.target.value)}>
                <option value="">{t('dash_selectPerson')}</option>
                <PersonOptions groups={people} />
              </Select>
            </div>
          ) : (
            <div className="w-56">
              <Select aria-label={t('filter_branchAria')} value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
                <option value="">{t('newTicket_selectBranchOption')}</option>
                {departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </Select>
            </div>
          )}
          <input
            type="date"
            aria-label={t('dash_dayAria')}
            value={day}
            onChange={(e) => e.target.value && setDate(new Date(`${e.target.value}T00:00:00`))}
            className="rounded-md border border-slate-300 px-3 py-2 text-sm"
          />
          <Button variant="secondary" onClick={() => setDate(new Date())}>
            {t('common_today')}
          </Button>
        </div>
      </div>
      {mode === 'branch' ? (
        !departmentId ? (
          <p className="py-10 text-center text-sm text-slate-600">{t('dash_chooseBranch')}</p>
        ) : loading ? (
          <p className="py-10 text-center text-sm text-slate-600">{t('common_loading')}</p>
        ) : error ? (
          <ErrorText>{error}</ErrorText>
        ) : !branchData || branchData.users.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-600">{t('dash_noEmployees')}</p>
        ) : (
          <div className="space-y-5">
            {branchData.users.map((u) => (
              <div key={u.userId} className="border-t border-slate-100 pt-3 first:border-t-0 first:pt-0">
                <div className="mb-1 text-xs font-semibold text-slate-600">{u.userName}</div>
                <GanttChart entries={u.entries} date={date} />
              </div>
            ))}
          </div>
        )
      ) : !personId ? (
        <p className="py-10 text-center text-sm text-slate-600">{t('dash_choosePerson')}</p>
      ) : loading ? (
        <p className="py-10 text-center text-sm text-slate-600">{t('common_loading')}</p>
      ) : error ? (
        <ErrorText>{error}</ErrorText>
      ) : (
        <GanttChart entries={personTimeline.data?.entries ?? []} date={date} />
      )}
    </Card>
  );
}
