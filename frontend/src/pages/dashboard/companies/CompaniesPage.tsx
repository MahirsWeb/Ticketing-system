import { Fragment, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { subDays } from 'date-fns';
import { companyReportsApi, type CompanySort } from '../../../api/companyReports';
import { useCompanyClientStats, useCompanyList } from '../../../api/companyReportQueries';
import { DateRangePicker } from '../../../components/DateRangePicker';
import { Pager } from '../../../components/Pager';
import { Button, Card, ErrorText, Input } from '../../../components/ui';
import { useLanguage } from '../../../i18n/LanguageContext';
import type { CompanyRowDto } from '../../../types';
import { Change } from './Change';
import { hoursText, percentText } from './figureText';

const PAGE_SIZE = 25;
const SHORT_LIST = 5;

function validDate(text: string | null): Date | null {
  if (!text) return null;
  const d = new Date(text);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** The people of one company, under its row. */
function ClientRows({ companyId, from, to, colSpan }: { companyId: string; from: string; to: string; colSpan: number }) {
  const { t } = useLanguage();
  const people = useCompanyClientStats({ from, to }, companyId);
  return (
    <tr className="bg-slate-50">
      <td colSpan={colSpan} className="px-6 py-3">
        {people.isPending ? (
          <span className="text-xs text-slate-600">{t('common_loading')}</span>
        ) : people.isError ? (
          <ErrorText>{t('dash_partialFail')}</ErrorText>
        ) : people.data.length === 0 ? (
          <span className="text-xs text-slate-600">{t('co_noTickets')}</span>
        ) : (
          <ul className="space-y-1 text-sm">
            {people.data.map((c) => (
              <li key={c.clientId} className="flex justify-between gap-3">
                <span>{c.name}</span>
                <span className="flex gap-3 text-xs text-slate-600">
                  <span>{t('co_countTotal', { count: c.total })}</span>
                  <span className="text-amber-700">{t('dash_countOpen', { count: c.open })}</span>
                  <span className="text-green-700">{t('dash_countClosed', { count: c.closed })}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
      </td>
    </tr>
  );
}

/** A short list under the table: the companies that grew the most, or fell the most, against the period before. */
function ShortList({ title, hint, items }: { title: string; hint: string; items: CompanyRowDto[] }) {
  const { t } = useLanguage();
  return (
    <Card className="p-5">
      <h2 className="text-sm font-bold uppercase tracking-wide text-slate-600">{title}</h2>
      <p className="mb-3 text-xs text-slate-600">{hint}</p>
      {items.length === 0 ? (
        <p className="text-sm text-slate-600">{t('co_nothingToShow')}</p>
      ) : (
        <ol className="space-y-2">
          {items.map((c) => (
            <li key={c.companyId} className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
              <span className="font-medium text-slate-800">{c.name}</span>
              <span className="flex items-center gap-3">
                {c.total === 0 && <span className="rounded bg-slate-200 px-1.5 text-xs text-slate-700">{t('co_quiet')}</span>}
                <span className="text-xs text-slate-600">{t('co_nowBefore', { now: c.total, before: c.previousTotal })}</span>
              </span>
            </li>
          ))}
        </ol>
      )}
    </Card>
  );
}

/**
 * "See more": every company in a table, with a search box, a sort on every column and pages; a row opens to the people of the company. Under the table the companies
 * that grew the most and fell the most. The period comes with the address (from the tab it was opened from) and can be changed here.
 */
export default function CompaniesPage() {
  const { t } = useLanguage();
  const [search, setSearch] = useSearchParams();
  const [to, setTo] = useState(() => validDate(search.get('to')) ?? new Date());
  const [from, setFrom] = useState(() => validDate(search.get('from')) ?? subDays(new Date(), 30));
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<CompanySort>('total');
  const [descending, setDescending] = useState(true);
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  // The search starts a little after the person stops typing, and always from the first page.
  useEffect(() => {
    const timer = setTimeout(() => {
      setQuery(text.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(timer);
  }, [text]);

  const range = { from: from.toISOString(), to: to.toISOString() };
  const list = useCompanyList({ ...range, search: query || undefined, sort, descending, page, pageSize: PAGE_SIZE });
  const growing = useCompanyList({ ...range, sort: 'growth', descending: true, page: 1, pageSize: SHORT_LIST });
  const falling = useCompanyList({ ...range, sort: 'growth', descending: false, page: 1, pageSize: SHORT_LIST });

  function sortBy(key: CompanySort) {
    if (key === sort) setDescending((d) => !d);
    else {
      setSort(key);
      setDescending(key !== 'name');
    }
    setPage(1);
  }

  async function download() {
    setExporting(true);
    try {
      await companyReportsApi.exportXlsx(range);
    } finally {
      setExporting(false);
    }
  }

  const columns: { key: CompanySort; label: string; right?: boolean }[] = [
    { key: 'name', label: t('co_colCompany') },
    { key: 'total', label: t('co_colTotal'), right: true },
    { key: 'previous', label: t('co_colPrevious'), right: true },
    { key: 'growth', label: t('co_colChange'), right: true },
    { key: 'open', label: t('co_colOpen'), right: true },
    { key: 'avgresolution', label: t('co_colResolution'), right: true },
    { key: 'sla', label: t('co_colSla'), right: true },
    { key: 'share', label: t('co_colShare'), right: true },
  ];
  const data = list.data;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const grew = (growing.data?.items ?? []).filter((c) => c.total > c.previousTotal);
  const fell = (falling.data?.items ?? []).filter((c) => c.total < c.previousTotal);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link className="text-sm font-medium text-blue-700 hover:underline" to="/?tab=companies">
            ← {t('co_backToDashboard')}
          </Link>
          <h1 className="text-xl font-bold text-slate-900">{t('co_allCompanies')}</h1>
          <p className="text-xs text-slate-600">{t('co_cohortCreated')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <DateRangePicker
            from={from}
            to={to}
            onChange={(f, tt) => {
              setFrom(f ?? subDays(new Date(), 30));
              setTo(tt ?? new Date());
              setPage(1);
              setSearch({}, { replace: true });
            }}
          />
          <Button variant="secondary" onClick={download} disabled={exporting}>
            {exporting ? t('dash_exporting') : <><span aria-hidden>⬇</span> {t('dash_exportXlsx')}</>}
          </Button>
        </div>
      </div>

      <ErrorText>{list.isError ? t('dash_partialFail') : null}</ErrorText>

      <Card className="p-5">
        <Input aria-label={t('co_searchAria')} placeholder={t('co_searchPlaceholder')} value={text} onChange={(e) => setText(e.target.value)} className="mb-4 max-w-sm" />
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-600">
                {columns.map((c) => (
                  <th key={c.key} className={`px-2 py-2 ${c.right ? 'text-right' : ''}`} aria-sort={sort === c.key ? (descending ? 'descending' : 'ascending') : 'none'}>
                    <button type="button" className="font-semibold uppercase hover:text-blue-700" onClick={() => sortBy(c.key)}>
                      {c.label}
                      {sort === c.key ? (descending ? ' ▼' : ' ▲') : ''}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data?.items.map((c) => (
                <Fragment key={c.companyId}>
                  <tr className="border-b border-slate-100">
                    <td className="px-2 py-2">
                      <button type="button" aria-expanded={open === c.companyId} className="font-medium text-blue-700 hover:underline" onClick={() => setOpen(open === c.companyId ? null : c.companyId)}>
                        {open === c.companyId ? '▾' : '▸'} {c.name}
                      </button>
                    </td>
                    <td className="px-2 py-2 text-right font-semibold">{c.total}</td>
                    <td className="px-2 py-2 text-right">{c.previousTotal}</td>
                    <td className="px-2 py-2 text-right">
                      <Change total={c.total} previous={c.previousTotal} />
                    </td>
                    <td className="px-2 py-2 text-right text-amber-700">{c.open}</td>
                    <td className="px-2 py-2 text-right">{hoursText(c.avgResolutionHours, c.closed)}</td>
                    <td className="px-2 py-2 text-right">{percentText(c.slaComplianceRate)}</td>
                    <td className="px-2 py-2 text-right">{c.sharePercent}%</td>
                  </tr>
                  {open === c.companyId && <ClientRows companyId={c.companyId} from={range.from} to={range.to} colSpan={columns.length} />}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
        {data && data.items.length === 0 && <p className="py-4 text-sm text-slate-600">{t('co_noCompanyFound')}</p>}
        {data && <p className="mt-3 text-xs text-slate-600">{t('co_listSummary', { count: data.totalCount, total: data.totalTickets })}</p>}
        <Pager page={page} totalPages={totalPages} onPage={setPage} />
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <ShortList title={t('co_fastestGrowing')} hint={t('co_fastestGrowingHint')} items={grew} />
        <ShortList title={t('co_biggestDrop')} hint={t('co_biggestDropHint')} items={fell} />
      </div>
    </div>
  );
}
