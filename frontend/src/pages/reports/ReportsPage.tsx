import { lazy, Suspense } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Tabs } from '../../components/Tabs';
import { useLanguage } from '../../i18n/LanguageContext';
import { TicketsReportTab } from './TicketsReportTab';

const EmployeesReportTab = lazy(() => import('./EmployeesReportTab').then((m) => ({ default: m.EmployeesReportTab })));

type TabKey = 'tickets' | 'employees';

/**
 * The reports of staff (Admin and Employee). The tabs are added as the reports are: the one about tickets and the one about employees. Which is open is in
 * the address (`?tab=employees`), so that a reload lands on the same one. The server turns a client away from every figure, so the page itself needs no
 * check beyond the route.
 */
export default function ReportsPage() {
  const { t } = useLanguage();
  const [params, setParams] = useSearchParams();
  const active: TabKey = params.get('tab') === 'employees' ? 'employees' : 'tickets';
  return (
    <div>
      <Tabs
        className="mb-4"
        label={t('rp_tabsAria')}
        active={active}
        tabs={[
          { key: 'tickets', label: t('rp_tabTickets') },
          { key: 'employees', label: t('rp_tabEmployees') },
        ]}
        onSelect={(key) => setParams(key === 'employees' ? { tab: 'employees' } : {}, { replace: true })}
      />
      {active === 'tickets' ? (
        <TicketsReportTab />
      ) : (
        <Suspense fallback={<p role="status" className="py-8 text-center text-sm text-slate-600">{t('common_loading')}</p>}>
          <EmployeesReportTab />
        </Suspense>
      )}
    </div>
  );
}
