import { useMemo, useState } from 'react';
import { format, subMonths } from 'date-fns';
import { documentsApi, type MonthlyParams } from '../../api/documents';
import { useCompanyMonthly } from '../../api/documentQueries';
import { useWorkload } from '../../api/employeeReportQueries';
import { useCompanies } from '../../api/lookupQueries';
import { Combobox } from '../../components/Combobox';
import { Button, Card, ErrorText, Select } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import { useAuthStore } from '../../store/authStore';
import type { CompanyMonthlyAudience } from '../../types';
import { CardHeading } from '../dashboard/companies/CompanyCards';
import { TimesheetCard } from './EmployeeCards';
import { CompanyMonthlyDocument } from './CompanyMonthlyDocument';

/**
 * The "Records" tab: the documents. The monthly report of a company (the internal version for the archive, the client version for sending; shown on the screen,
 * printed from the browser ("Save as PDF") or downloaded as xlsx) and the monthly work sheet of a person. A document is the state at the moment it is made.
 */
export function EvidenceTab() {
  const { t } = useLanguage();
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';

  const [companyId, setCompanyId] = useState<string | null>(null);
  const [month, setMonth] = useState(() => format(subMonths(new Date(), 1), 'yyyy-MM'));
  const [audience, setAudience] = useState<CompanyMonthlyAudience>('internal');
  const [includeHours, setIncludeHours] = useState(false);
  const [requested, setRequested] = useState<MonthlyParams | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const [person, setPerson] = useState('');

  const companies = useCompanies();
  const options = useMemo(() => (companies.data ?? []).map((c) => ({ id: c.id, label: c.name })), [companies.data]);
  const report = useCompanyMonthly(requested);
  const workload = useWorkload({});

  function paramsFromForm(): MonthlyParams | null {
    if (!companyId || !month) return null;
    const [year, m] = month.split('-').map(Number);
    return { companyId, year, month: m, audience, includeHours: audience === 'client' ? includeHours : undefined };
  }

  async function download() {
    const params = paramsFromForm();
    if (!params) return;
    setBusy(true);
    setFailed(false);
    try {
      await documentsApi.monthlyXlsx(params);
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  const ready = !!companyId && !!month;
  const personId = isAdmin ? person || undefined : user?.id;

  return (
    <div className="space-y-6">
      <div className="print:hidden">
        <h1 className="text-xl font-bold text-slate-900">{t('ev_title')}</h1>
        <p className="text-xs text-slate-600">{t('ev_note')}</p>
      </div>

      <Card className="p-5 print:hidden">
        <CardHeading title={t('mr_title')} note={t('mr_note')} />
        <div className="flex flex-wrap items-end gap-4">
          <div className="w-64">
            <Combobox
              aria-label={t('co_companyAria')}
              options={options}
              value={companyId}
              busy={companies.isPending}
              placeholder={t('co_companyPlaceholder')}
              noResultsText={t('picker_noCompanies')}
              onChange={setCompanyId}
            />
          </div>
          <label className="flex items-center gap-2 text-sm text-slate-700">
            {t('em_month')}
            <input type="month" value={month} onChange={(e) => setMonth(e.target.value)} className="rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm text-slate-900" />
          </label>
          <fieldset className="flex items-center gap-4 text-sm text-slate-700">
            <legend className="sr-only">{t('mr_version')}</legend>
            <label className="flex items-center gap-1.5">
              <input type="radio" name="audience" checked={audience === 'internal'} onChange={() => setAudience('internal')} />
              {t('mr_versionInternal')}
            </label>
            <label className="flex items-center gap-1.5">
              <input type="radio" name="audience" checked={audience === 'client'} onChange={() => setAudience('client')} />
              {t('mr_versionClient')}
            </label>
          </fieldset>
          {audience === 'client' && (
            <label className="flex items-center gap-1.5 text-sm text-slate-700">
              <input type="checkbox" checked={includeHours} onChange={(e) => setIncludeHours(e.target.checked)} />
              {t('mr_includeHours')}
            </label>
          )}
        </div>
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Button onClick={() => setRequested(paramsFromForm())} disabled={!ready}>
            {t('mr_make')}
          </Button>
          <Button variant="secondary" onClick={download} disabled={!ready || busy}>
            {busy ? t('dash_exporting') : <><span aria-hidden>⬇</span> {t('em_download')}</>}
          </Button>
          {report.data && (
            <Button variant="secondary" onClick={() => window.print()}>
              {t('tk_print')}
            </Button>
          )}
          {!ready && <span className="text-xs text-slate-600">{t('mr_chooseFirst')}</span>}
        </div>
        <ErrorText>{failed ? t('em_downloadFailed') : null}</ErrorText>
        <ErrorText>{report.isError ? t('mr_failed') : null}</ErrorText>
      </Card>

      {report.data && <CompanyMonthlyDocument data={report.data} />}

      <div className="space-y-3 print:hidden">
        {isAdmin && (
          <div className="flex items-center gap-2">
            <label htmlFor="evidence-person" className="text-sm text-slate-700">
              {t('em_person')}
            </label>
            <div className="w-56">
              <Select id="evidence-person" value={person} onChange={(e) => setPerson(e.target.value)}>
                <option value="">{t('em_choosePerson')}</option>
                {(workload.data?.people ?? []).map((p) => (
                  <option key={p.userId} value={p.userId}>
                    {p.name}
                  </option>
                ))}
              </Select>
            </div>
          </div>
        )}
        <TimesheetCard userId={personId} />
      </div>
    </div>
  );
}
