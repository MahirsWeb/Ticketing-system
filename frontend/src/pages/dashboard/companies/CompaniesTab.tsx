import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { subDays } from 'date-fns';
import { companyReportsApi } from '../../../api/companyReports';
import {
  useCompanyBranches,
  useCompanyCategories,
  useCompanyClientStats,
  useCompanyClosedBy,
  useCompanyConversation,
  useCompanyOpenAge,
  useCompanyRepeated,
  useCompanyRhythm,
  useCompanySummary,
  useCompanyTimeSpent,
  useCompanyTopics,
  useQuietCompanies,
  useTopCompanies,
} from '../../../api/companyReportQueries';
import { useCompanies } from '../../../api/lookupQueries';
import { useCompanyClientChoices } from '../../../api/userQueries';
import { Combobox } from '../../../components/Combobox';
import { DateRangePicker } from '../../../components/DateRangePicker';
import { Button, ErrorText } from '../../../components/ui';
import { useLanguage } from '../../../i18n/LanguageContext';
import { BranchesCard, BreakdownCard, ClientsCard, ClosedByCard, TopCompaniesCard } from './CompanyCards';
import { CompanyFigures } from './CompanyFigures';
import { ConversationCard, OpenAgeCard, QuietCard, RepeatedCard, RhythmCard, TimeSpentCard } from './InsightCards';

function ExportButton({ params }: { params: { from: string; to: string; companyId?: string; clientId?: string } }) {
  const { t } = useLanguage();
  const [exporting, setExporting] = useState(false);
  async function download() {
    setExporting(true);
    try {
      await companyReportsApi.exportXlsx(params);
    } finally {
      setExporting(false);
    }
  }
  return (
    <Button variant="secondary" onClick={download} disabled={exporting}>
      {exporting ? t('dash_exporting') : <><span aria-hidden>⬇</span> {t('dash_exportXlsx')}</>}
    </Button>
  );
}

/**
 * The second tab: the companies. Nothing chosen shows the ten companies with the most tickets and what is across them; a company shows its own figures; a
 * person of a company shows theirs next to those of the company. The person cannot be chosen without the company and goes when the company does.
 * Admin and Employee see the same figures here, from all branches.
 */
export function CompaniesTab() {
  const { t } = useLanguage();
  const [to, setTo] = useState(() => new Date());
  const [from, setFrom] = useState(() => subDays(new Date(), 30));
  const [companyId, setCompanyId] = useState<string | null>(null);
  const [clientId, setClientId] = useState<string | null>(null);

  const companies = useCompanies();
  const choices = useCompanyClientChoices(companyId);
  const companyOptions = useMemo(() => (companies.data ?? []).map((c) => ({ id: c.id, label: c.name })), [companies.data]);
  const clientOptions = useMemo(() => (choices.data ?? []).map((c) => ({ id: c.id, label: c.name, secondary: c.email })), [choices.data]);

  const range = { from: from.toISOString(), to: to.toISOString() };
  const params = { ...range, companyId: companyId ?? undefined, clientId: companyId ? clientId ?? undefined : undefined };
  const top = useTopCompanies(range, !companyId);
  const summary = useCompanySummary(params, !!companyId);
  const companySummary = useCompanySummary({ ...range, companyId: companyId ?? undefined }, !!companyId && !!clientId);
  const people = useCompanyClientStats(range, companyId ?? undefined);
  const topics = useCompanyTopics(params);
  const categories = useCompanyCategories(params);
  const branches = useCompanyBranches(params);
  const closedBy = useCompanyClosedBy(params);
  const openAge = useCompanyOpenAge(params);
  const timeSpent = useCompanyTimeSpent(params);
  const conversation = useCompanyConversation(params);
  const rhythm = useCompanyRhythm(params);
  const repeated = useCompanyRepeated(params);
  const quiet = useQuietCompanies(range, !companyId);

  const failed = [top, summary, people, topics, categories, branches, closedBy, openAge, timeSpent, conversation, rhythm, repeated, quiet].some((q) => q.isError);
  const seeMore = (
    <Link className="text-sm font-medium text-blue-700 hover:underline" to={`/dashboard/companies?from=${encodeURIComponent(range.from)}&to=${encodeURIComponent(range.to)}`}>
      {t('co_seeMore')} →
    </Link>
  );

  return (
    <div className="space-y-6">
      <div className="sticky top-0 z-20 -mx-4 flex flex-wrap items-center justify-between gap-3 border-b border-slate-300 bg-slate-200 px-4 py-3">
        <div>
          <h1 className="text-xl font-bold text-slate-900">{t('co_title')}</h1>
          <p className="text-xs text-slate-600">{t('co_allBranches')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="w-56">
            <Combobox
              aria-label={t('co_companyAria')}
              options={companyOptions}
              value={companyId}
              busy={companies.isPending}
              placeholder={t('co_companyPlaceholder')}
              noResultsText={t('picker_noCompanies')}
              onChange={(id) => {
                setCompanyId(id);
                setClientId(null);
              }}
            />
          </div>
          <div className="w-56">
            <Combobox
              aria-label={t('co_clientAria')}
              options={clientOptions}
              value={clientId}
              disabled={!companyId}
              busy={!!companyId && choices.isPending}
              placeholder={companyId ? t('co_clientPlaceholder') : t('co_clientNeedsCompany')}
              noResultsText={t('picker_noClients')}
              onChange={setClientId}
            />
          </div>
          <DateRangePicker
            from={from}
            to={to}
            onChange={(f, tt) => {
              setFrom(f ?? subDays(new Date(), 30));
              setTo(tt ?? new Date());
            }}
          />
          <ExportButton params={params} />
        </div>
      </div>

      <ErrorText>{failed ? t('dash_partialFail') : null}</ErrorText>

      {!companyId && top.data && <TopCompaniesCard data={top.data} onPick={(c) => setCompanyId(c.companyId)} seeMore={seeMore} />}

      {companyId && summary.data && (
        <CompanyFigures
          summary={summary.data}
          company={clientId ? companySummary.data : undefined}
          peopleCount={clientId ? people.data?.length : undefined}
        />
      )}

      {companyId && !clientId && people.data && <ClientsCard items={people.data} onPick={setClientId} canPick={(id) => clientOptions.some((o) => o.id === id)} />}

      {!companyId && <p className="text-xs text-slate-600">{t('co_scopeTop')}</p>}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {closedBy.data && <ClosedByCard data={closedBy.data} />}
        {topics.data && <BreakdownCard title={t('co_byTopic')} items={topics.data} />}
        {categories.data && <BreakdownCard title={t('co_byCategory')} items={categories.data} />}
        {companyId && branches.data && <BranchesCard items={branches.data} />}
        {openAge.data && <OpenAgeCard data={openAge.data} />}
        {timeSpent.data && <TimeSpentCard data={timeSpent.data} />}
        {conversation.data && <ConversationCard data={conversation.data} />}
        {repeated.data && <RepeatedCard data={repeated.data} />}
        {rhythm.data && <RhythmCard data={rhythm.data} />}
        {!companyId && quiet.data && <QuietCard data={quiet.data} />}
      </div>
    </div>
  );
}
