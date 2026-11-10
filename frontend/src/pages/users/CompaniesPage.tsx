import { Link } from 'react-router-dom';
import { useCompaniesAdmin } from '../../api/adminLookupQueries';
import { Card, ErrorText, StatusPill } from '../../components/ui';
import { AddCompanyForm } from './AddCompanyForm';
import { useLanguage } from '../../i18n/LanguageContext';

export default function CompaniesPage() {
  const { t } = useLanguage();
  const companies = useCompaniesAdmin();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">{t('companies_title')}</h1>
        <p className="mt-1 text-sm text-slate-600">{t('companies_subtitle')}</p>
      </div>

      <AddCompanyForm />

      {companies.isError && <ErrorText>{t('companies_loadError')}</ErrorText>}
      <Card className="divide-y divide-slate-100">
        {(companies.data ?? []).map((c) => (
          <Link
            key={c.id}
            to={`/companies/${c.id}`}
            className="flex items-center justify-between gap-3 px-4 py-3 text-sm transition hover:bg-slate-50"
          >
            <div>
              <div className="font-medium text-slate-800">{c.name}</div>
              {(c.address || c.contactInfo) && (
                <div className="mt-0.5 text-xs text-slate-600">{[c.address, c.contactInfo].filter(Boolean).join(' · ')}</div>
              )}
            </div>
            <div className="flex items-center gap-3">
              <StatusPill active={c.isActive} />
              <span aria-hidden className="text-slate-300">→</span>
            </div>
          </Link>
        ))}
        {companies.isPending && <div className="px-4 py-6 text-center text-sm text-slate-600">{t('companies_loading')}</div>}
        {companies.isSuccess && companies.data.length === 0 && <div className="px-4 py-6 text-center text-sm text-slate-600">{t('companies_empty')}</div>}
      </Card>
    </div>
  );
}
