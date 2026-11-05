import { Link } from 'react-router-dom';
import { useCompaniesAdmin } from '../../api/adminLookupQueries';
import { Card, ErrorText, StatusPill } from '../../components/ui';
import { AddCompanyForm } from './AddCompanyForm';

export default function CompaniesPage() {
  const companies = useCompaniesAdmin();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Client companies</h1>
        <p className="mt-1 text-sm text-slate-500">Click a company to see its users and tickets.</p>
      </div>

      <AddCompanyForm />

      {companies.isError && <ErrorText>The companies could not be loaded. Try again in a moment.</ErrorText>}
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
                <div className="mt-0.5 text-xs text-slate-500">{[c.address, c.contactInfo].filter(Boolean).join(' · ')}</div>
              )}
            </div>
            <div className="flex items-center gap-3">
              <StatusPill active={c.isActive} />
              <span className="text-slate-300">→</span>
            </div>
          </Link>
        ))}
        {companies.isPending && <div className="px-4 py-6 text-center text-sm text-slate-500">Loading companies…</div>}
        {companies.isSuccess && companies.data.length === 0 && <div className="px-4 py-6 text-center text-sm text-slate-500">No companies yet.</div>}
      </Card>
    </div>
  );
}
