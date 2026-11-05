import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Tabs } from '../../components/Tabs';
import { useAuthStore } from '../../store/authStore';
import { AccountsSection } from './AccountsSection';
import CompaniesPage from './CompaniesPage';

type Section = 'users' | 'companies';
const SECTIONS: { key: Section; label: string }[] = [
  { key: 'users', label: 'Users' },
  { key: 'companies', label: 'Companies' },
];

export default function UsersPage() {
  const isAdmin = useAuthStore((s) => s.user?.role) === 'Admin';
  const [searchParams] = useSearchParams();
  const [section, setSection] = useState<Section>(searchParams.get('tab') === 'companies' ? 'companies' : 'users');

  return (
    <div className="space-y-6">
      <h1 className="text-xl font-bold text-slate-900">Users</h1>
      <Tabs label="Section" tabs={SECTIONS} active={section} onSelect={setSection} />
      {section === 'companies' ? <CompaniesPage /> : <AccountsSection isAdmin={isAdmin} preselectedCompanyId={searchParams.get('companyId')} />}
    </div>
  );
}
