import { Tabs } from '../../components/Tabs';
import { useLanguage } from '../../i18n/LanguageContext';
import { TicketsReportTab } from './TicketsReportTab';

/**
 * The reports of staff (Admin and Employee). The tabs are added as the reports are: for now, the one about tickets. The server turns a client away from
 * every figure, so the page itself needs no check beyond the route.
 */
export default function ReportsPage() {
  const { t } = useLanguage();
  return (
    <div>
      <Tabs className="mb-4" label={t('rp_tabsAria')} active="tickets" tabs={[{ key: 'tickets', label: t('rp_tabTickets') }]} onSelect={() => undefined} />
      <TicketsReportTab />
    </div>
  );
}
