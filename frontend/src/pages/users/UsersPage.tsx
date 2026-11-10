import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Tabs } from '../../components/Tabs';
import { useAuthStore } from '../../store/authStore';
import { AccountsSection } from './AccountsSection';
import CompaniesPage from './CompaniesPage';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';

type Section = 'users' | 'companies';
const SECTIONS: { key: Section; labelKey: TranslationKey }[] = [
  { key: 'users', labelKey: 'nav_users' },
  { key: 'companies', labelKey: 'common_companies' },
];

export default function UsersPage() {
  const { t } = useLanguage();
  const isAdmin = useAuthStore((s) => s.user?.role) === 'Admin';
  const [searchParams] = useSearchParams();
  const [section, setSection] = useState<Section>(searchParams.get('tab') === 'companies' ? 'companies' : 'users');

  return (
    <div className="space-y-6">
      <h1 className="text-xl font-bold text-slate-900">{t('nav_users')}</h1>
      <Tabs label={t('users_sectionLabel')} tabs={SECTIONS.map((x) => ({ key: x.key, label: t(x.labelKey) }))} active={section} onSelect={setSection} />
      {section === 'companies' ? <CompaniesPage /> : <AccountsSection isAdmin={isAdmin} preselectedCompanyId={searchParams.get('companyId')} />}
    </div>
  );
}
