import { lazy, Suspense } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Tabs } from '../../components/Tabs';
import { useLanguage } from '../../i18n/LanguageContext';
import { TicketsReportTab } from './TicketsReportTab';

const EmployeesReportTab = lazy(() => import('./EmployeesReportTab').then((m) => ({ default: m.EmployeesReportTab })));
const EvidenceTab = lazy(() => import('./EvidenceTab').then((m) => ({ default: m.EvidenceTab })));

type TabKey = 'tickets' | 'employees' | 'evidence';

/**
 * The reports of staff (Admin and Employee). The tabs are added as the reports are: the one about tickets and the one about employees. Which is open is in
 * the address (`?tab=employees`), so that a reload lands on the same one. The server turns a client away from every figure, so the page itself needs no
 * check beyond the route.
 */
export default function ReportsPage() {
  const { t } = useLanguage();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab');
  const active: TabKey = tab === 'employees' ? 'employees' : tab === 'evidence' ? 'evidence' : 'tickets';
  return (
    <div>
      <Tabs
        className="mb-4 print:hidden"
        label={t('rp_tabsAria')}
        active={active}
        tabs={[
          { key: 'tickets', label: t('rp_tabTickets') },
          { key: 'employees', label: t('rp_tabEmployees') },
          { key: 'evidence', label: t('rp_tabEvidence') },
        ]}
        onSelect={(key) => setParams(key === 'tickets' ? {} : { tab: key }, { replace: true })}
      />
      {active === 'tickets' ? (
        <TicketsReportTab />
      ) : (
        <Suspense fallback={<p role="status" className="py-8 text-center text-sm text-slate-600">{t('common_loading')}</p>}>
          {active === 'employees' ? <EmployeesReportTab /> : <EvidenceTab />}
        </Suspense>
      )}
    </div>
  );
}
