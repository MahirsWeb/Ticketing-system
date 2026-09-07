import { useEffect, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { format } from 'date-fns';
import clsx from 'clsx';
import { useAuthStore } from '../../store/authStore';
import { usersApi } from '../../api/users';
import { lookupsApi } from '../../api/lookups';
import { ticketsApi } from '../../api/tickets';
import type { CompanyItemFull, CreatedUserResponse, DepartmentItemFull, SubBranchItemFull, TicketListItem, UserListItemDto, UserRole } from '../../types';
import { Button, Card, ErrorText, Input, Label, PriorityBadge, Select, Spinner, StatusBadge, StatusPill } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { UserAvatar } from '../../components/UserAvatar';
import { TempPasswordModal } from './TempPasswordModal';

export default function UserDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const currentUser = useAuthStore((s) => s.user);
  const isAdmin = currentUser?.role === 'Admin';

  const [target, setTarget] = useState<UserListItemDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);

  const [tickets, setTickets] = useState<TicketListItem[]>([]);
  const [ticketsLoading, setTicketsLoading] = useState(true);

  const [companies, setCompanies] = useState<CompanyItemFull[]>([]);
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [subBranchesByDept, setSubBranchesByDept] = useState<Record<string, SubBranchItemFull[]>>({});

  const [editing, setEditing] = useState(false);
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [accountType, setAccountType] = useState<UserRole>('Client');
  const [companyId, setCompanyId] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [subBranchId, setSubBranchId] = useState('');
  const [editError, setEditError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [result, setResult] = useState<CreatedUserResponse | null>(null);
  const [confirmRegenerate, setConfirmRegenerate] = useState(false);
  const [confirmDeactivate, setConfirmDeactivate] = useState(false);
  const [avatarUploading, setAvatarUploading] = useState(false);
  const [avatarError, setAvatarError] = useState<string | null>(null);

  const needsBranch = accountType === 'Employee';
  const availableSubBranches = departmentId ? subBranchesByDept[departmentId] ?? [] : [];

  function loadUser() {
    if (!id) return;
    setLoading(true);
    usersApi
      .getById(id)
      .then((u) => {
        setTarget(u);
        setFirstName(u.firstName);
        setLastName(u.lastName);
        setAccountType(u.role as UserRole);
        setCompanyId(u.companyId ?? '');
        setDepartmentId(u.departmentId ?? '');
        setSubBranchId(u.subBranchId ?? '');
      })
      .catch(() => setNotFound(true))
      .finally(() => setLoading(false));
  }

  useEffect(() => {
    loadUser();
    lookupsApi.companies().then(setCompanies);
    lookupsApi.departments().then(setDepartments);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  useEffect(() => {
    if (needsBranch && departmentId && !subBranchesByDept[departmentId]) {
      lookupsApi.subBranches(departmentId).then((items) => setSubBranchesByDept((prev) => ({ ...prev, [departmentId]: items })));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [departmentId, needsBranch]);

  useEffect(() => {
    if (!id || !target) return;
    setTicketsLoading(true);
    const params = target.role === 'Client' ? { clientId: id, pageSize: 100 } : { assignedToUserId: id, pageSize: 100 };
    ticketsApi
      .list(params)
      .then((res) => setTickets(res.items))
      .finally(() => setTicketsLoading(false));
  }, [id, target]);

  function startEdit() {
    if (!target) return;
    setFirstName(target.firstName);
    setLastName(target.lastName);
    setAccountType(target.role as UserRole);
    setCompanyId(target.companyId ?? '');
    setDepartmentId(target.departmentId ?? '');
    setSubBranchId(target.subBranchId ?? '');
    setEditError(null);
    setEditing(true);
  }

  async function handleSaveEdit(e: React.FormEvent) {
    e.preventDefault();
    if (!id) return;
    setEditError(null);

    if (accountType === 'Client' && !companyId) {
      setEditError('Please select a company.');
      return;
    }
    if (needsBranch && !departmentId) {
      setEditError('Please select a branch.');
      return;
    }
    if (needsBranch && availableSubBranches.length > 0 && !subBranchId) {
      setEditError('Please select a sub-branch.');
      return;
    }

    setSaving(true);
    try {
      await usersApi.updateUser(id, {
        firstName,
        lastName,
        role: accountType,
        companyId: accountType === 'Client' ? companyId : undefined,
        departmentId: needsBranch ? departmentId : undefined,
        subBranchId: needsBranch ? subBranchId || undefined : undefined,
      });
      setEditing(false);
      loadUser();
    } catch (err: any) {
      setEditError(err?.response?.data?.message ?? 'Could not save changes.');
    } finally {
      setSaving(false);
    }
  }

  async function handleRegenerateConfirmed() {
    if (!id) return;
    setConfirmRegenerate(false);
    setResult(await usersApi.regenerateTempPassword(id));
  }

  async function handleToggleActive() {
    if (!target) return;
    if (target.isActive) {
      setConfirmDeactivate(true);
      return;
    }
    await usersApi.setActive(target.id, true);
    loadUser();
  }

  async function handleConfirmDeactivate() {
    if (!target) return;
    setConfirmDeactivate(false);
    await usersApi.setActive(target.id, false);
    loadUser();
  }

  async function handleAvatarUpload(file: File) {
    if (!id) return;
    setAvatarError(null);
    setAvatarUploading(true);
    try {
      await usersApi.uploadProfilePicture(id, file);
      loadUser();
    } catch (err: any) {
      setAvatarError(err?.response?.data?.message ?? 'Could not upload the picture.');
    } finally {
      setAvatarUploading(false);
    }
  }

  if (loading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }
  if (notFound || !target) return <p>User not found.</p>;

  const canEditAvatar = isAdmin || currentUser?.id === target.id;
  const orgLabel = target.role === 'Client' ? 'Organization' : 'Branch';
  const orgValue =
    target.role === 'Client'
      ? target.companyName ?? '—'
      : [target.departmentName, target.subBranchName].filter(Boolean).join(' / ') || '—';

  const profileFields: { label: string; value: ReactNode }[] = [
    { label: 'Email', value: target.email },
    ...(target.role === 'Client' && target.phoneNumber ? [{ label: 'Phone', value: target.phoneNumber }] : []),
    { label: orgLabel, value: orgValue },
    { label: 'Status', value: <StatusPill active={target.isActive} /> },
    { label: 'Email verified', value: target.emailConfirmed ? 'Yes' : 'No' },
    { label: 'Created', value: format(new Date(target.createdAtUtc), 'dd.MM.yyyy') },
    { label: 'Last login', value: target.lastLoginAtUtc ? format(new Date(target.lastLoginAtUtc), 'dd.MM.yyyy HH:mm') : 'Never' },
  ];
  const profileColumns: (typeof profileFields)[] = [];
  for (let i = 0; i < profileFields.length; i += 3) profileColumns.push(profileFields.slice(i, i + 3));

  return (
    <div className="space-y-6">
      <div>
        <Link to="/users" className="text-xs font-medium text-blue-700 hover:underline">
          ← All users
        </Link>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <h1 className="text-xl font-bold text-slate-900">
            {target.firstName} {target.lastName}
          </h1>
          <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-semibold text-slate-600">{target.role}</span>
        </div>
      </div>

      <Card className="p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="flex flex-wrap items-center gap-5">
            <div>
              <UserAvatar
                url={target.profilePictureUrl}
                editable={canEditAvatar}
                uploading={avatarUploading}
                onUpload={handleAvatarUpload}
              />
              {avatarError && <p className="mt-1 max-w-[6rem] text-[11px] text-red-600">{avatarError}</p>}
            </div>
            <div className="grid grid-cols-1 gap-x-10 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
              {profileColumns.map((col, i) => (
                <div key={i} className="space-y-3">
                  {col.map((f) => (
                    <div key={f.label}>
                      <div className="text-xs font-semibold uppercase text-slate-400">{f.label}</div>
                      <div className="text-slate-800">{f.value}</div>
                    </div>
                  ))}
                </div>
              ))}
            </div>
          </div>
          {isAdmin && (
            <button
              type="button"
              onClick={() => (editing ? setEditing(false) : startEdit())}
              className="flex shrink-0 items-center gap-1.5 rounded-md border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-50"
            >
              ✏️ {editing ? 'Close' : 'Edit account details'}
            </button>
          )}
        </div>

        <div className={clsx('grid transition-all duration-200 ease-out', editing ? 'grid-rows-[1fr] opacity-100' : 'grid-rows-[0fr] opacity-0')}>
          <div className="overflow-hidden">
            <form onSubmit={handleSaveEdit} className="mt-5 grid grid-cols-1 gap-4 border-t border-slate-100 pt-5 sm:grid-cols-2">
              <div>
                <Label>First name</Label>
                <Input required maxLength={25} value={firstName} onChange={(e) => setFirstName(e.target.value)} />
              </div>
              <div>
                <Label>Last name</Label>
                <Input required maxLength={30} value={lastName} onChange={(e) => setLastName(e.target.value)} />
              </div>
              <div>
                <Label>Account type</Label>
                <Select value={accountType} onChange={(e) => setAccountType(e.target.value as UserRole)}>
                  <option value="Client">Client</option>
                  <option value="Employee">Employee</option>
                  <option value="Admin">Admin</option>
                </Select>
              </div>
              {accountType === 'Client' && (
                <div>
                  <Label>Company</Label>
                  <Select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
                    <option value="">Select a company…</option>
                    {companies.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                      </option>
                    ))}
                  </Select>
                </div>
              )}
              {needsBranch && (
                <div>
                  <Label>Branch</Label>
                  <Select
                    value={departmentId}
                    onChange={(e) => {
                      setDepartmentId(e.target.value);
                      setSubBranchId('');
                    }}
                  >
                    <option value="">Select a branch…</option>
                    {departments.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name} ({d.email})
                      </option>
                    ))}
                  </Select>
                </div>
              )}
              {needsBranch && availableSubBranches.length > 0 && (
                <div>
                  <Label>Sub-branch</Label>
                  <Select value={subBranchId} onChange={(e) => setSubBranchId(e.target.value)}>
                    <option value="">Select a sub-branch…</option>
                    {availableSubBranches.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </Select>
                </div>
              )}
              <div className="sm:col-span-2">
                <ErrorText>{editError}</ErrorText>
                <div className="flex gap-2">
                  <Button type="submit" disabled={saving}>
                    {saving ? 'Saving…' : 'Save'}
                  </Button>
                  <Button type="button" variant="secondary" onClick={() => setEditing(false)}>
                    Cancel
                  </Button>
                </div>
              </div>
            </form>
          </div>
        </div>

        {isAdmin && (
          <div className="mt-5 flex flex-wrap gap-x-5 gap-y-2 border-t border-slate-100 pt-4 text-xs">
            <button className="font-medium text-slate-500 hover:underline" onClick={handleToggleActive}>
              {target.isActive ? 'Deactivate account' : 'Reactivate account'}
            </button>
            <button className="font-medium text-blue-700 hover:underline" onClick={() => setConfirmRegenerate(true)}>
              Regenerate temp password
            </button>
          </div>
        )}
      </Card>

      <div>
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-500">
          Tickets {target.role === 'Client' ? 'submitted' : 'assigned'} ({tickets.length})
        </h2>
        <Card className="overflow-hidden">
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
                  <th className="px-3 py-1.5">{target.role === 'Client' ? 'Assigned to' : 'Client'}</th>
                  <th className="px-3 py-1.5">Created</th>
                </tr>
              </thead>
              <tbody>
                {tickets.map((t, i) => (
                  <tr
                    key={t.id}
                    className={clsx(
                      'cursor-pointer border-t border-slate-100 transition-colors hover:bg-slate-200',
                      i % 2 === 1 && 'bg-slate-100'
                    )}
                    onClick={() => navigate(`/tickets/${t.id}`)}
                  >
                    <td className="whitespace-nowrap px-3 py-1.5">
                      <Link to={`/tickets/${t.id}`} className="font-medium text-blue-700 hover:underline" onClick={(e) => e.stopPropagation()}>
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
                    <td className="whitespace-nowrap px-3 py-1.5">{target.role === 'Client' ? t.assignedToName ?? '—' : t.clientName}</td>
                    <td className="whitespace-nowrap px-3 py-1.5 text-slate-500">{format(new Date(t.createdAt), 'dd.MM.yyyy HH:mm')}</td>
                  </tr>
                ))}
                {tickets.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-3 py-8 text-center text-slate-400">
                      No tickets found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}
        </Card>
      </div>

      {result && <TempPasswordModal result={result} onClose={() => setResult(null)} />}
      {confirmRegenerate && (
        <ConfirmDialog
          title="Regenerate temporary password?"
          message={`This immediately invalidates ${target.firstName} ${target.lastName}'s current password and emails them a new temporary one. Are you sure?`}
          confirmLabel="Regenerate"
          danger
          onConfirm={handleRegenerateConfirmed}
          onCancel={() => setConfirmRegenerate(false)}
        />
      )}
      {confirmDeactivate && (
        <ConfirmDialog
          title="Deactivate this account?"
          message={`${target.firstName} ${target.lastName} will no longer be able to log in and will stop receiving any emails from the app. You can reactivate this account at any time.`}
          confirmLabel="Deactivate"
          danger
          onConfirm={handleConfirmDeactivate}
          onCancel={() => setConfirmDeactivate(false)}
        />
      )}
    </div>
  );
}
