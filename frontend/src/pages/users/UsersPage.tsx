import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import clsx from 'clsx';
import { useAuthStore } from '../../store/authStore';
import { usersApi } from '../../api/users';
import { lookupsApi } from '../../api/lookups';
import type { CompanyItemFull, CreatedUserResponse, DepartmentItemFull, SubBranchItemFull, UserListItemDto, UserRole } from '../../types';
import { Button, Card, ErrorText, Input, Label, Select, StatusPill } from '../../components/ui';
import { TempPasswordModal } from './TempPasswordModal';
import CompaniesPage from './CompaniesPage';

export default function UsersPage() {
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';
  const canSeeEmployees = isAdmin; // non-admin staff only manage clients here
  const [searchParams] = useSearchParams();
  const preselectedCompanyId = searchParams.get('companyId');
  const [section, setSection] = useState<'users' | 'companies'>(searchParams.get('tab') === 'companies' ? 'companies' : 'users');

  const [tab, setTab] = useState<'clients' | 'employees'>('clients');
  const [users, setUsers] = useState<UserListItemDto[]>([]);
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

  function refreshUsers() {
    usersApi.list().then(setUsers);
  }

  async function getSubBranches(deptId: string): Promise<SubBranchItemFull[]> {
    if (subBranchesByDept[deptId]) return subBranchesByDept[deptId];
    const items = await lookupsApi.subBranches(deptId);
    setSubBranchesByDept((prev) => ({ ...prev, [deptId]: items }));
    return items;
  }

  useEffect(() => {
    refreshUsers();
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

  const clients = users.filter((u) => u.role === 'Client');
  const employees = users.filter((u) => u.role !== 'Client');
  const activeTab = canSeeEmployees ? tab : 'clients';
  const visibleUsers = activeTab === 'clients' ? clients : employees;

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
            Clients ({clients.length})
          </button>
          <button
            onClick={() => setTab('employees')}
            className={`px-3 py-2 text-sm font-medium ${activeTab === 'employees' ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
          >
            Employees & Admins ({employees.length})
          </button>
        </div>
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
                  No accounts here yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </Card>

      {result && <TempPasswordModal result={result} onClose={() => setResult(null)} />}
        </>
      )}
    </div>
  );
}
