import { Card } from '../../../components/ui';
import { useLanguage } from '../../../i18n/LanguageContext';
import type { CompanySummaryDto } from '../../../types';
import { Change } from './Change';
import { hoursText, percentText } from './figureText';

function Figure({ label, value, definition, accent, compare, children }: { label: string; value: string | number; definition: string; accent?: string; compare?: string; children?: React.ReactNode }) {
  return (
    <Card className="p-4" title={definition}>
      <div className="text-xs font-semibold uppercase tracking-wide text-slate-600">{label}</div>
      <div className={`mt-1 text-2xl font-bold ${accent ?? 'text-slate-900'}`}>{value}</div>
      {children}
      {compare && <div className="mt-1 text-xs text-slate-600">{compare}</div>}
      <p className="mt-2 text-[11px] leading-snug text-slate-600">{definition}</p>
    </Card>
  );
}

/**
 * The cards of one company, or of one person of it. For a person each card says, under the figure, what the company as a whole has (`company`): the tickets
 * against the average of the people of the company who reported any (`peopleCount`), the resolution time and the SLA against those of the company.
 */
export function CompanyFigures({ summary, company, peopleCount }: { summary: CompanySummaryDto; company?: CompanySummaryDto; peopleCount?: number }) {
  const { t } = useLanguage();
  const perPerson = company && peopleCount && peopleCount > 0 ? Math.round((company.total / peopleCount) * 10) / 10 : null;
  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
      <Figure
        label={t('co_figTotal')}
        value={summary.total}
        definition={t('co_figTotalHint')}
        compare={perPerson != null ? t('co_vsAverage', { value: perPerson }) : undefined}
      >
        <div className="mt-0.5">
          <Change total={summary.total} previous={summary.previousTotal} />
        </div>
      </Figure>
      <Figure label={t('kpi_new')} value={summary.new} definition={t('co_figNewHint')} />
      <Figure label={t('kpi_openInProgress')} value={summary.open} accent="text-amber-700" definition={t('co_figOpenHint')} />
      <Figure label={t('kpi_closed')} value={summary.closed} accent="text-green-700" definition={t('co_figClosedHint')} />
      <Figure
        label={t('kpi_avgResolution')}
        value={hoursText(summary.avgResolutionHours, summary.closed)}
        definition={t('co_figResolutionHint')}
        compare={company ? t('co_vsCompany', { value: hoursText(company.avgResolutionHours, company.closed) }) : undefined}
      />
      <Figure
        label={t('kpi_slaCompliance')}
        value={percentText(summary.slaComplianceRate)}
        accent="text-blue-700"
        definition={t('co_figSlaHint')}
        compare={company ? t('co_vsCompany', { value: percentText(company.slaComplianceRate) }) : undefined}
      />
    </div>
  );
}
