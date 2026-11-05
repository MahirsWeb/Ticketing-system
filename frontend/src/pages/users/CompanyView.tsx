import { useState } from 'react';
import { useCompanyClients } from '../../api/userQueries';
import { Tabs } from '../../components/Tabs';
import type { CompanyItemFull } from '../../types';
import { CompanyEditPanel } from './CompanyEditPanel';
import { CompanyHeader } from './CompanyHeader';
import { CompanyTicketsTab } from './CompanyTicketsTab';
import { CompanyUsersTab } from './CompanyUsersTab';
import { useCompanyEdit } from './useCompanyEdit';

type CompanyTab = 'users' | 'tickets';

/**
 * A company that has been loaded: its details and the editing of them, and its users and tickets in two tabs. Its clients are asked for here
 * because both tabs use them (the count in the first, the choice of client in the second).
 */
export function CompanyView({ company }: { company: CompanyItemFull }) {
  const [tab, setTab] = useState<CompanyTab>('users');
  const [editing, setEditing] = useState(false);
  const edit = useCompanyEdit(company, () => setEditing(false));
  const clients = useCompanyClients(company.id);

  const tabs: { key: CompanyTab; label: string }[] = [
    { key: 'users', label: `Users (${clients.data ? clients.data.length : '…'})` },
    { key: 'tickets', label: 'Tickets' },
  ];

  return (
    <div className="space-y-6">
      <CompanyHeader company={company} editing={editing} onToggleEdit={() => (editing ? edit.cancel() : setEditing(true))} />
      <CompanyEditPanel edit={edit} open={editing} />
      <Tabs label="Company" tabs={tabs} active={tab} onSelect={setTab} />
      {tab === 'users' ? <CompanyUsersTab companyId={company.id} clients={clients} /> : <CompanyTicketsTab companyId={company.id} clients={clients} />}
    </div>
  );
}
