import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import clsx from 'clsx';
import { useAuthStore } from '../../store/authStore';
import { usersApi } from '../../api/users';
import { lookupsApi } from '../../api/lookups';
import type { CompanyItemFull, CreatedUserResponse, DepartmentItemFull, SubBranchItemFull, UserPageDto, UserRole } from '../../types';
import { Button, Card, ErrorText, Input, Label, Select, StatusPill } from '../../components/ui';
import { TempPasswordModal } from './TempPasswordModal';
import CompaniesPage from './CompaniesPage';

const PAGE_SIZE = 25;

export default function UsersPage() {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const canSeeEmployees = isAdmin; // non-admin staff only manage clients here
  const [searchParams] = useSearchParams();
  const preselectedCompanyId = searchParams.get('companyId');
  const [section, setSection] = useState<'users' | 'companies'>(searchParams.get('tab') === 'companies' ? 'companies' : 'users');

  const [tab, setTab] = useState<'clients' | 'employees'>('clients');
  const [pageData, setPageData] = useState<UserPageDto | null>(null);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = useState(1);
  const [reloadKey, setReloadKey] = useState(0);
  const [listError, setListError] = useState<string | null>(null);
  const [companies, setCompanies] = useState<CompanyItemFull[]>([]);
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [subBranchesByDept, setSubBranchesByDept] = useState<Record<string, SubBranchItemFull[]>>({});
  const [result, setResult] = useState<CreatedUserResponse | null>(null);

  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [accountType, setAccountType] = useState<'Client' | UserRole>('Client');
  const [companyId, setCompanyId] = useState(preselectedCompanyId ?? '');
  const [departmentId, setDepartmentId] = useState('');
  const [subBranchId, setSubBranchId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const needsBranch = accountType === 'Employee';
  const availableSubBranches = departmentId ? subBranchesByDept[departmentId] ?? [] : [];

  const activeTab = canSeeEmployees ? tab : 'clients';

  function refreshUsers() {
    setReloadKey((k) => k + 1);
  }

  // Typing in the search box should not fire a request per keystroke.
  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSearch(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  // A different tab or search starts again from the first page.
  useEffect(() => {
    setPage(1);
  }, [activeTab, debouncedSearch]);

  // Only the visible page is fetched; an older request that is still running is cancelled when a newer one starts,
  // so a slow answer can never overwrite a newer one.
  useEffect(() => {
    if (section !== 'users') return;
    const controller = new AbortController();
    setListError(null);
    usersApi
      .listPaged({ group: activeTab === 'clients' ? 'clients' : 'staff', search: debouncedSearch || undefined, page, pageSize: PAGE_SIZE }, controller.signal)
      .then(setPageData)
      .catch((err) => {
        if (controller.signal.aborted || err?.code === 'ERR_CANCELED') return;
        setListError(err?.response?.data?.message ?? 'Could not load the accounts.');
      });
    return () => controller.abort();
  }, [section, activeTab, debouncedSearch, page, reloadKey]);

  async function getSubBranches(deptId: string): Promise<SubBranchItemFull[]> {
    if (subBranchesByDept[deptId]) return subBranchesByDept[deptId];
    const items = await lookupsApi.subBranches(deptId);
    setSubBranchesByDept((prev) => ({ ...prev, [deptId]: items }));
    return items;
  }

  useEffect(() => {
    lookupsApi.companies().then(setCompanies);
    lookupsApi.departments().then(setDepartments);
  }, []);

  useEffect(() => {
    if (needsBranch && departmentId) getSubBranches(departmentId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [departmentId, needsBranch]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (accountType === 'Client' && !companyId) {
      setError('Please select a company.');
      return;
    }
    if (needsBranch && !departmentId) {
      setError('Please select a branch.');
      return;
    }
    if (needsBranch && availableSubBranches.length > 0 && !subBranchId) {
      setError('Please select a sub-branch.');
      return;
    }

    setLoading(true);
    try {
      let created: CreatedUserResponse;
      if (accountType === 'Client') {
        created = await usersApi.createClient(firstName, lastName, email, companyId);
      } else {
        created = await usersApi.createEmployee(
          firstName,
          lastName,
          email,
          accountType,
          needsBranch ? departmentId : undefined,
          needsBranch ? subBranchId || undefined : undefined
        );
      }
      setResult(created);
      setFirstName('');
      setLastName('');
      setEmail('');
      setCompanyId('');
      setDepartmentId('');
      setSubBranchId('');
      refreshUsers();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not create the account.');
    } finally {
      setLoading(false);
    }
  }

  const visibleUsers = pageData?.items ?? [];
  const totalPages = Math.max(1, Math.ceil((pageData?.totalCount ?? 0) / PAGE_SIZE));

  return (
    <div className="space-y-6">
      <h1 className="text-xl font-bold text-slate-900">Users</h1>

      <div className="flex gap-2 border-b border-slate-200">
        <button
          onClick={() => setSection('users')}
          className={`px-3 py-2 text-sm font-medium ${section === 'users' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          Users
        </button>
        <button
          onClick={() => setSection('companies')}
          className={`px-3 py-2 text-sm font-medium ${section === 'companies' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          Companies
        </button>
      </div>

      {section === 'companies' ? (
        <CompaniesPage />
      ) : (
        <>
      <Card className="p-5">
        <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">Create account</h2>
        <form onSubmit={handleCreate} className="grid grid-cols-2 gap-4">
          <div>
            <Label>First name</Label>
            <Input required maxLength={25} value={firstName} onChange={(e) => setFirstName(e.target.value)} />
          </div>
          <div>
            <Label>Last name</Label>
            <Input required maxLength={30} value={lastName} onChange={(e) => setLastName(e.target.value)} />
          </div>
          <div>
            <Label>Email</Label>
            <Input type="email" required maxLength={100} value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
          <div>
            <Label>Account type</Label>
            <Select value={accountType} onChange={(e) => setAccountType(e.target.value as any)}>
              <option value="Client">Client</option>
              {isAdmin && (
                <>
                  <option value="Employee">Employee</option>
                  <option value="Admin">Admin</option>
                </>
              )}
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
          <div className="col-span-2">
            <ErrorText>{error}</ErrorText>
            <Button type="submit" disabled={loading}>
              {loading ? 'Creating…' : 'Create account'}
            </Button>
          </div>
        </form>
      </Card>

      {canSeeEmployees && (
        <div className="flex gap-2 border-b border-slate-200">
          <button
            onClick={() => setTab('clients')}
            className={`px-3 py-2 text-sm font-medium ${activeTab === 'clients' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
          >
            Clients ({pageData?.clientCount ?? '…'})
          </button>
          <button
            onClick={() => setTab('employees')}
            className={`px-3 py-2 text-sm font-medium ${activeTab === 'employees' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
          >
            Employees & Admins ({pageData?.staffCount ?? '…'})
          </button>
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Input
          className="max-w-sm"
          placeholder="Search by name or company — or type a full email"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <span className="text-xs text-slate-400">
          {pageData ? `${pageData.totalCount} ${pageData.totalCount === 1 ? 'account' : 'accounts'}${debouncedSearch ? ' match' : ''}` : ''}
        </span>
      </div>
      <ErrorText>{listError}</ErrorText>
      {!isAdmin && (
        <p className="text-xs text-slate-400">
          You see the clients who have tickets in your branch. To open a ticket for any other client, use "New ticket on behalf" and
          type their full email address.
        </p>
      )}

      <Card className="overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
            <tr>
              <th className="px-4 py-2">Name</th>
              <th className="px-4 py-2">Email</th>
              <th className="px-4 py-2">{activeTab === 'employees' ? 'Branch' : 'Company'}</th>
              <th className="px-4 py-2">Status</th>
            </tr>
          </thead>
          <tbody>
            {visibleUsers.map((u, i) => (
              <tr
                key={u.id}
                className={clsx('border-t border-slate-100 transition-colors hover:bg-slate-200', i % 2 === 1 && 'bg-slate-100')}
              >
                <td className="px-4 py-2">
                  <Link to={`/users/${u.id}`} className="font-medium text-blue-700 hover:underline">
                    {u.firstName} {u.lastName}
                  </Link>
                </td>
                <td className="px-4 py-2">{u.email}</td>
                <td className="px-4 py-2 text-slate-500">{(activeTab === 'employees' ? u.departmentName : u.companyName) ?? '—'}</td>
                <td className="px-4 py-2">
                  <StatusPill active={u.isActive} />
                </td>
              </tr>
            ))}
            {visibleUsers.length === 0 && (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-slate-400">
                  {debouncedSearch ? 'No accounts match your search.' : 'No accounts here yet.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </Card>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2">
          <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </Button>
          <span className="text-sm text-slate-600">
            Page {page} of {totalPages}
          </span>
          <Button variant="secondary" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
            Next
          </Button>
        </div>
      )}

      {result && <TempPasswordModal result={result} onClose={() => setResult(null)} />}
        </>
      )}
    </div>
  );
}
