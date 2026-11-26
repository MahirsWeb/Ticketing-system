import { lazy, Suspense } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Tabs } from '../../components/Tabs';
import { useLanguage } from '../../i18n/LanguageContext';
import DashboardPage from './DashboardPage';

const CompaniesTab = lazy(() => import('./companies/CompaniesTab').then((m) => ({ default: m.CompaniesTab })));

type TabKey = 'overview' | 'companies';

/**
 * The home page of staff: two tabs. The first is the overview, exactly as it has always been (DashboardPage, untouched); the second is the statistics of the
 * companies. Which one is open is in the address (`?tab=companies`), so that the way back from "See more" and a reload land on the same tab. Clients never
 * get here (they are sent to their tickets), and the server turns them away from the figures of the second tab as well.
 */
export default function DashboardShell() {
  const { t } = useLanguage();
  const [params, setParams] = useSearchParams();
  const active: TabKey = params.get('tab') === 'companies' ? 'companies' : 'overview';
  return (
    <div>
      <Tabs
        className="mb-4"
        label={t('co_tabsAria')}
        active={active}
        tabs={[
          { key: 'overview', label: t('co_tabOverview') },
          { key: 'companies', label: t('co_tabCompanies') },
        ]}
        onSelect={(key) => setParams(key === 'companies' ? { tab: 'companies' } : {}, { replace: true })}
      />
      {active === 'overview' ? (
        <DashboardPage />
      ) : (
        <Suspense fallback={<p role="status" className="py-8 text-center text-sm text-slate-600">{t('common_loading')}</p>}>
          <CompaniesTab />
        </Suspense>
      )}
    </div>
  );
}
