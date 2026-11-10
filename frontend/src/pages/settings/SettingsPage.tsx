import { useState } from 'react';
import clsx from 'clsx';
import { lookupsApi } from '../../api/lookups';
import { useHelpTopicsAdmin } from '../../api/adminLookupQueries';
import { BranchesTab } from './BranchesTab';
import { CategoriesAndSlaTab } from './CategoriesAndSlaTab';
import { NotificationsTab } from './NotificationsTab';
import { SimpleLookupTab } from './SimpleLookupTab';
import type { TranslationKey } from '../../i18n/translations';
import { LIMITS } from './settingsHelpers';
import { useLanguage } from '../../i18n/LanguageContext';

type TabKey = 'branches' | 'helpTopics' | 'categoriesAndSla' | 'notifications';

const TABS: { key: TabKey; labelKey: TranslationKey; icon: string }[] = [
  { key: 'branches', labelKey: 'settingsTab_branches', icon: '🏢' },
  { key: 'helpTopics', labelKey: 'settingsTab_helpTopics', icon: '🏷️' },
  { key: 'categoriesAndSla', labelKey: 'settingsTab_categoriesSla', icon: '🗂️' },
  { key: 'notifications', labelKey: 'settingsTab_notifications', icon: '⏰' },
];

function HelpTopicsTab() {
  const { t: tr } = useLanguage();
  const helpTopics = useHelpTopicsAdmin();
  return (
    <SimpleLookupTab
      itemNoun={tr('noun_helpTopic')}
      maxLength={LIMITS.helpTopic}
      list={helpTopics}
      onCreate={lookupsApi.createHelpTopic}
      onUpdate={lookupsApi.updateHelpTopic}
      onDelete={lookupsApi.deleteHelpTopic}
      deleteWarning={tr('lookup_deleteWarning', { noun: tr('noun_helpTopic') })}
      deactivateWarning={tr('lookup_deactivateWarning', { noun: tr('noun_helpTopic') })}
    />
  );
}

/**
 * The values an administrator maintains: the branches, the help topics, the categories and SLA plans that agents pick from when opening a
 * ticket, the overdue warnings and the documentation of the AI assistant. Each section asks for its own data when it is opened.
 */
export default function SettingsPage() {
  const { t: tr } = useLanguage();
  const [tab, setTab] = useState<TabKey>('branches');

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">{tr('settings_title')}</h1>
        <p className="mt-1 text-sm text-slate-600">
          {tr('settings_subtitle')}
        </p>
      </div>

      <div className="flex gap-1 overflow-x-auto border-b border-slate-200" role="tablist" aria-label={tr('settings_sectionsAria')}>
        {TABS.map((t) => (
          <button
            key={t.key}
            role="tab"
            aria-selected={tab === t.key}
            onClick={() => setTab(t.key)}
            className={clsx(
              'flex shrink-0 items-center gap-2 whitespace-nowrap border-b-2 px-4 py-2.5 text-sm font-semibold transition',
              tab === t.key ? 'border-blue-700 text-blue-700' : 'border-transparent text-slate-600 hover:text-slate-700'
            )}
          >
            <span>{t.icon}</span>
            {tr(t.labelKey)}
          </button>
        ))}
      </div>

      {tab === 'branches' && <BranchesTab />}
      {tab === 'helpTopics' && <HelpTopicsTab />}
      {tab === 'categoriesAndSla' && <CategoriesAndSlaTab />}
      {tab === 'notifications' && <NotificationsTab />}
    </div>
  );
}
