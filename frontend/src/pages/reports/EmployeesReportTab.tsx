import { useState } from 'react';
import { subDays } from 'date-fns';
import { useDependency, useHoursOfPeople, useHoursOfPerson, useSpecialization, useWorkload } from '../../api/employeeReportQueries';
import { useDepartments } from '../../api/lookupQueries';
import { DateRangePicker } from '../../components/DateRangePicker';
import { ErrorText, Select } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import { useAuthStore } from '../../store/authStore';
import { DependencyCard, HoursCard, HoursDetailCard, SpecializationCard, WorkloadCard } from './EmployeeCards';

/**
 * The "Employees" tab: reports about people. Admin sees everybody (and may narrow to a branch, and choose a person for the details); an employee sees only
 * themself. Which companies depend on one person is for Admin only.
 */
export function EmployeesReportTab() {
  const { t } = useLanguage();
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const [to, setTo] = useState(() => new Date());
  const [from, setFrom] = useState(() => subDays(new Date(), 30));
  const [departmentId, setDepartmentId] = useState('');
  const [chosen, setChosen] = useState('');
  const { data: departments = [] } = useDepartments({ enabled: isAdmin });

  const branch = isAdmin && departmentId ? departmentId : undefined;
  const personId = isAdmin ? chosen || undefined : user?.id;
  const range = { from: from.toISOString(), to: to.toISOString() };

  const workload = useWorkload({ departmentId: branch });
  const hours = useHoursOfPeople({ ...range, departmentId: branch });
  const detail = useHoursOfPerson(personId, range);
  const specialization = useSpecialization(personId, range);
  const dependency = useDependency(isAdmin);
  const failed = [workload, hours, detail, specialization, dependency].some((q) => q.isError);

  return (
    <div className="space-y-6">
      <div className="sticky top-0 z-20 -mx-4 flex flex-wrap items-center justify-between gap-3 border-b border-slate-300 bg-slate-200 px-4 py-3">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{t('em_title')}</h1>
          <p className="text-xs text-slate-600">{isAdmin ? t('em_scopeAdmin') : t('em_scopeSelf')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {isAdmin && (
            <>
              <div className="w-48">
                <Select
                  aria-label={t('filter_branchAria')}
                  value={departmentId}
                  onChange={(e) => {
                    setDepartmentId(e.target.value);
                    setChosen('');
                  }}
                >
                  <option value="">{t('filter_allBranches')}</option>
                  {departments.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </Select>
              </div>
              <div className="w-52">
                <Select aria-label={t('em_person')} value={chosen} onChange={(e) => setChosen(e.target.value)}>
                  <option value="">{t('em_choosePerson')}</option>
                  {(workload.data?.people ?? []).map((p) => (
                    <option key={p.userId} value={p.userId}>
                      {p.name}
                    </option>
                  ))}
                </Select>
              </div>
            </>
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

      <section aria-label={t('em_loadSection')} className="space-y-4">
        <div>
          <h2 className="text-base font-bold text-slate-900">{t('em_loadSection')}</h2>
          <p className="text-xs text-slate-600">{t('rp_nowNote')}</p>
        </div>
        {workload.data && <WorkloadCard data={workload.data} />}
      </section>

      <section aria-label={t('rp_periodSection')} className="space-y-4">
        <div>
          <h2 className="text-base font-bold text-slate-900">{t('rp_periodSection')}</h2>
          <p className="text-xs text-slate-600">{t('rp_periodNote')}</p>
        </div>
        {hours.data && <HoursCard data={hours.data} selectedId={personId ?? null} onPick={isAdmin ? setChosen : undefined} />}
        {personId && detail.data && <HoursDetailCard data={detail.data} />}
        {personId && specialization.data && <SpecializationCard data={specialization.data} />}
        {!personId && <p className="text-sm text-slate-600">{t('em_chooseFirst')}</p>}
      </section>

      {isAdmin && dependency.data && (
        <section aria-label={t('em_dependency')} className="space-y-4">
          <DependencyCard data={dependency.data} />
        </section>
      )}
    </div>
  );
}
