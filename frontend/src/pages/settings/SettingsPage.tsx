import { useState } from 'react';
import clsx from 'clsx';
import { lookupsApi } from '../../api/lookups';
import { useHelpTopicsAdmin } from '../../api/adminLookupQueries';
import { BranchesTab } from './BranchesTab';
import { CategoriesAndSlaTab } from './CategoriesAndSlaTab';
import { NotificationsTab } from './NotificationsTab';
import { SimpleLookupTab } from './SimpleLookupTab';
import { LIMITS } from './settingsHelpers';

type TabKey = 'branches' | 'helpTopics' | 'categoriesAndSla' | 'notifications';

const TABS: { key: TabKey; label: string; icon: string }[] = [
  { key: 'branches', label: 'Branches', icon: '🏢' },
  { key: 'helpTopics', label: 'Help Topics', icon: '🏷️' },
  { key: 'categoriesAndSla', label: 'Categories & SLA', icon: '🗂️' },
  { key: 'notifications', label: 'Notifications', icon: '⏰' },
];

function HelpTopicsTab() {
  const helpTopics = useHelpTopicsAdmin();
  return (
    <SimpleLookupTab
      itemNoun="help topic"
      maxLength={LIMITS.helpTopic}
      list={helpTopics}
      onCreate={lookupsApi.createHelpTopic}
      onUpdate={lookupsApi.updateHelpTopic}
      onDelete={lookupsApi.deleteHelpTopic}
      deleteWarning="This help topic will be permanently deleted. A help topic that tickets already use cannot be deleted (deactivate it instead), so nothing in the history of any ticket is lost."
      deactivateWarning="Tickets currently using this help topic keep it and are completely unaffected — nothing changes for existing tickets or statistics. This only removes it from the picker when opening a new ticket."
    />
  );
}

/**
 * The values an administrator maintains: the branches, the help topics, the categories and SLA plans that agents pick from when opening a
 * ticket, the overdue warnings and the documentation of the AI assistant. Each section asks for its own data when it is opened.
 */
export default function SettingsPage() {
  const [tab, setTab] = useState<TabKey>('branches');

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Settings</h1>
        <p className="mt-1 text-sm text-slate-600">
          Manage the dropdown values agents choose from when opening a ticket. Only Admins can change these.
        </p>
      </div>

      <div className="flex gap-1 overflow-x-auto border-b border-slate-200" role="tablist" aria-label="Settings sections">
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
            {t.label}
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
