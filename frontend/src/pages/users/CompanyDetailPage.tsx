import { useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { format } from 'date-fns';
import clsx from 'clsx';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { ticketsApi } from '../../api/tickets';
import type { CompanyItemFull, TicketListItem, UserListItemDto } from '../../types';
import { Button, Card, ErrorText, Input, Label, PriorityBadge, Select, Spinner, StatusBadge, StatusPill } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';

const NAME_MAX = 120;
const ADDRESS_MAX = 100;
const CONTACT_MAX = 120;

export default function CompanyDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [company, setCompany] = useState<CompanyItemFull | null>(null);
  const [tab, setTab] = useState<'users' | 'tickets'>('users');
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [clients, setClients] = useState<UserListItemDto[]>([]);
  const [userSearch, setUserSearch] = useState('');

  const [tickets, setTickets] = useState<TicketListItem[]>([]);
  const [ticketsLoading, setTicketsLoading] = useState(false);
  const [clientFilter, setClientFilter] = useState('');

  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState('');
  const [editAddress, setEditAddress] = useState('');
  const [editContact, setEditContact] = useState('');
  const [editActive, setEditActive] = useState(true);
  const [editError, setEditError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [confirmActiveChange, setConfirmActiveChange] = useState(false);

  useEffect(() => {
    if (!id) return;
    setLoading(true);
    setLoadError(null);
    Promise.all([lookupsApi.getCompany(id), usersApi.list({ role: 'Client', companyId: id })])
      .then(([c, users]) => {
        setCompany(c);
        setEditName(c.name);
        setEditAddress(c.address ?? '');
        setEditContact(c.contactInfo ?? '');
        setEditActive(c.isActive);
        setClients(users);
      })
      .catch((err: any) => setLoadError(err?.response?.data?.message ?? 'Could not load this company.'))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => {
    if (!id || tab !== 'tickets') return;
    setTicketsLoading(true);
    ticketsApi
      .list({ companyId: id, clientId: clientFilter || undefined, pageSize: 100 })
      .then((res) => setTickets(res.items))
      .finally(() => setTicketsLoading(false));
  }, [id, tab, clientFilter]);

  const filteredClients = useMemo(() => {
    const q = userSearch.trim().toLowerCase();
    if (!q) return clients;
    return clients.filter((c) => `${c.firstName} ${c.lastName} ${c.email}`.toLowerCase().includes(q));
  }, [clients, userSearch]);

  function handleSaveClick() {
    if (!editName.trim() || !company) return;
    if (editActive !== company.isActive) {
      setConfirmActiveChange(true);
      return;
    }
    void handleSaveEdit();
  }

  async function handleSaveEdit() {
    if (!id || !editName.trim()) return;
    setSaving(true);
    setEditError(null);
    try {
      const updated = await lookupsApi.updateCompany(id, editName.trim(), editAddress || undefined, editContact || undefined, editActive);
      setCompany(updated);
      setEditing(false);
      setConfirmActiveChange(false);
    } catch (err: any) {
      setEditError(err?.response?.data?.message ?? 'Could not save changes.');
    } finally {
      setSaving(false);
    }
  }

  function handleCancelEdit() {
    if (!company) return;
    setEditName(company.name);
    setEditAddress(company.address ?? '');
    setEditContact(company.contactInfo ?? '');
    setEditActive(company.isActive);
    setEditError(null);
    setEditing(false);
  }

  if (loading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }
  if (loadError) return <ErrorText>{loadError}</ErrorText>;
  if (!company) return <p>Company not found.</p>;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link to="/users?tab=companies" className="text-xs font-medium text-blue-700 hover:underline">
            ← All companies
          </Link>
          <div className="mt-1 flex flex-wrap items-center gap-3">
            <h1 className="text-xl font-bold text-slate-900">{company.name}</h1>
            <StatusPill active={company.isActive} />
          </div>
          {(company.address || company.contactInfo) && (
            <p className="mt-1 text-sm text-slate-500">{[company.address, company.contactInfo].filter(Boolean).join(' · ')}</p>
          )}
        </div>
        <button
          type="button"
          onClick={() => (editing ? handleCancelEdit() : setEditing(true))}
          className="flex shrink-0 items-center gap-1.5 rounded-md border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-50"
        >
          ✏️ {editing ? 'Close' : 'Edit company details'}
        </button>
      </div>

      <div className={clsx('grid transition-all duration-200 ease-out', editing ? 'grid-rows-[1fr] opacity-100' : 'grid-rows-[0fr] opacity-0')}>
        <div className="overflow-hidden">
          <Card className="p-5">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              <div>
                <Label>Name</Label>
                <Input value={editName} maxLength={NAME_MAX} onChange={(e) => setEditName(e.target.value)} />
              </div>
              <div>
                <Label>Address</Label>
                <Input value={editAddress} maxLength={ADDRESS_MAX} onChange={(e) => setEditAddress(e.target.value)} />
              </div>
              <div>
                <Label>Contact info</Label>
                <Input value={editContact} maxLength={CONTACT_MAX} onChange={(e) => setEditContact(e.target.value)} />
              </div>
              <label className="flex items-center gap-2 text-sm text-slate-700">
                <input type="checkbox" checked={editActive} onChange={(e) => setEditActive(e.target.checked)} />
                Active
              </label>
              <div className="md:col-span-3">
                <ErrorText>{editError}</ErrorText>
                <div className="flex gap-2">
                  <Button onClick={handleSaveClick} disabled={saving || !editName.trim()}>
                    {saving ? 'Saving…' : 'Save'}
                  </Button>
                  <Button variant="secondary" onClick={handleCancelEdit}>
                    Cancel
                  </Button>
                </div>
              </div>
            </div>
          </Card>
        </div>
      </div>

      <div className="flex gap-2 border-b border-slate-200">
        <button
          onClick={() => setTab('users')}
          className={`px-3 py-2 text-sm font-medium ${tab === 'users' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          Users ({clients.length})
        </button>
        <button
          onClick={() => setTab('tickets')}
          className={`px-3 py-2 text-sm font-medium ${tab === 'tickets' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          Tickets
        </button>
      </div>

      {tab === 'users' && (
        <Card className="overflow-hidden">
          <div className="flex items-center justify-between gap-3 border-b border-slate-100 p-3">
            <Input placeholder="Search by name or email…" value={userSearch} onChange={(e) => setUserSearch(e.target.value)} className="max-w-xs" />
            <Link to={`/users?companyId=${id}`}>
              <Button variant="secondary" className="whitespace-nowrap text-xs">
                + Add user
              </Button>
            </Link>
          </div>
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
              <tr>
                <th className="px-4 py-2">Name</th>
                <th className="px-4 py-2">Email</th>
                <th className="px-4 py-2">Phone</th>
                <th className="px-4 py-2">Created</th>
                <th className="px-4 py-2">Status</th>
              </tr>
            </thead>
            <tbody>
              {filteredClients.map((c, i) => (
                <tr
                  key={c.id}
                  className={clsx('border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}
                >
                  <td className="px-4 py-2">
                    {c.firstName} {c.lastName}
                  </td>
                  <td className="px-4 py-2">{c.email}</td>
                  <td className="px-4 py-2">{c.phoneNumber ?? '—'}</td>
                  <td className="px-4 py-2 whitespace-nowrap text-slate-500">{format(new Date(c.createdAtUtc), 'dd.MM.yyyy')}</td>
                  <td className="px-4 py-2">
                    <StatusPill active={c.isActive} />
                  </td>
                </tr>
              ))}
              {filteredClients.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-4 py-8 text-center text-slate-400">
                    No matching users.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </Card>
      )}

      {tab === 'tickets' && (
        <Card className="overflow-hidden">
          <div className="flex items-center gap-2 border-b border-slate-100 p-3">
            <div className="w-64">
              <Select value={clientFilter} onChange={(e) => setClientFilter(e.target.value)}>
                <option value="">All clients</option>
                {clients.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.firstName} {c.lastName} ({c.email})
                  </option>
                ))}
              </Select>
            </div>
          </div>
          {ticketsLoading ? (
            <div className="flex justify-center py-10">
              <Spinner className="h-6 w-6 text-blue-700" />
            </div>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
                <tr>
                  <th className="px-3 py-1.5">#</th>
                  <th className="px-3 py-1.5">Title</th>
                  <th className="px-3 py-1.5">Status</th>
                  <th className="px-3 py-1.5">Priority</th>
                  <th className="px-3 py-1.5">Client</th>
                  <th className="px-3 py-1.5">Assigned to</th>
                  <th className="px-3 py-1.5">Created</th>
                </tr>
              </thead>
              <tbody>
                {tickets.map((t, i) => (
                  <tr
                    key={t.id}
                    className={clsx('border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}
                  >
                    <td className="whitespace-nowrap px-3 py-1.5">
                      <Link to={`/tickets/${t.id}`} className="font-medium text-blue-700 hover:underline">
                        #{t.ticketNumber}
                      </Link>
                    </td>
                    <td className="max-w-[220px] truncate px-3 py-1.5">{t.title}</td>
                    <td className="whitespace-nowrap px-3 py-1.5">
                      <StatusBadge status={t.status} />
                    </td>
                    <td className="whitespace-nowrap px-3 py-1.5">
                      <PriorityBadge priority={t.priority} />
                    </td>
                    <td className="whitespace-nowrap px-3 py-1.5">{t.clientName}</td>
                    <td className="whitespace-nowrap px-3 py-1.5">{t.assignedToName ?? '—'}</td>
                    <td className="whitespace-nowrap px-3 py-1.5 text-slate-500">{format(new Date(t.createdAt), 'dd.MM.yyyy HH:mm')}</td>
                  </tr>
                ))}
                {tickets.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-3 py-8 text-center text-slate-400">
                      No tickets found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}
        </Card>
      )}

      {confirmActiveChange && (
        <ConfirmDialog
          title={editActive ? 'Reactivate this company?' : 'Deactivate this company?'}
          message={
            editActive
              ? `"${editName}" will become active again and reappear in the company picker when creating a new client account.`
              : `Existing clients and tickets for "${editName}" are completely unaffected — nothing is deleted or changed for them. Deactivating only removes this company from the picker when creating a new client account.`
          }
          confirmLabel={editActive ? 'Reactivate' : 'Deactivate'}
          danger={!editActive}
          onConfirm={handleSaveEdit}
          onCancel={() => setConfirmActiveChange(false)}
        />
      )}
    </div>
  );
}
