import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { lookupsApi } from '../../api/lookups';
import type { CompanyItemFull } from '../../types';
import { Button, Card, ErrorText, Input, Label, StatusPill } from '../../components/ui';

export default function CompaniesPage() {
  const [companies, setCompanies] = useState<CompanyItemFull[]>([]);
  const [name, setName] = useState('');
  const [address, setAddress] = useState('');
  const [contactInfo, setContactInfo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  function refresh() {
    lookupsApi.companiesAdmin().then(setCompanies);
  }

  useEffect(refresh, []);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) {
      setError('Company name is required.');
      return;
    }
    setLoading(true);
    try {
      await lookupsApi.createCompany(name, address || undefined, contactInfo || undefined);
      setName('');
      setAddress('');
      setContactInfo('');
      refresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not create the company.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Client companies</h1>
        <p className="mt-1 text-sm text-slate-500">Click a company to see its users and tickets.</p>
      </div>

      <Card className="p-5">
        <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">Add a company</h2>
        <form onSubmit={handleCreate} className="grid grid-cols-3 gap-4">
          <div>
            <Label>Name</Label>
            <Input required maxLength={120} value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <Label>Address (optional)</Label>
            <Input maxLength={100} value={address} onChange={(e) => setAddress(e.target.value)} />
          </div>
          <div>
            <Label>Contact info (optional)</Label>
            <Input maxLength={120} value={contactInfo} onChange={(e) => setContactInfo(e.target.value)} />
          </div>
          <div className="col-span-3">
            <ErrorText>{error}</ErrorText>
            <Button type="submit" disabled={loading}>
              {loading ? 'Adding…' : 'Add company'}
            </Button>
          </div>
        </form>
      </Card>

      <Card className="divide-y divide-slate-100">
        {companies.map((c) => (
          <Link
            key={c.id}
            to={`/companies/${c.id}`}
            className="flex items-center justify-between gap-3 px-4 py-3 text-sm transition hover:bg-slate-50"
          >
            <div>
              <div className="font-medium text-slate-800">{c.name}</div>
              {(c.address || c.contactInfo) && (
                <div className="mt-0.5 text-xs text-slate-400">{[c.address, c.contactInfo].filter(Boolean).join(' · ')}</div>
              )}
            </div>
            <div className="flex items-center gap-3">
              <StatusPill active={c.isActive} />
              <span className="text-slate-300">→</span>
            </div>
          </Link>
        ))}
        {companies.length === 0 && <div className="px-4 py-6 text-center text-sm text-slate-400">No companies yet.</div>}
      </Card>
    </div>
  );
}
