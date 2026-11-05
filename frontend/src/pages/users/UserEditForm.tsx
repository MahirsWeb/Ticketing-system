import { useState } from 'react';
import { usersApi } from '../../api/users';
import { useCompanies, useDepartments, useSubBranches } from '../../api/lookupQueries';
import { Button, ErrorText, Field, Input, Label, Select } from '../../components/ui';
import type { UserListItemDto, UserRole } from '../../types';

/**
 * The form for changing the name and the place of an account. It starts from what the account is when it is drawn, so closing it and opening
 * it again forgets what was typed (the page draws a new one each time).
 */
export function UserEditForm({ user, onSaved, onCancel }: { user: UserListItemDto; onSaved: () => void; onCancel: () => void }) {
  const { data: companies = [] } = useCompanies();
  const { data: departments = [] } = useDepartments();
  const [firstName, setFirstName] = useState(user.firstName);
  const [lastName, setLastName] = useState(user.lastName);
  const [accountType, setAccountType] = useState<UserRole>(user.role);
  const [companyId, setCompanyId] = useState(user.companyId ?? '');
  const [departmentId, setDepartmentId] = useState(user.departmentId ?? '');
  const [subBranchId, setSubBranchId] = useState(user.subBranchId ?? '');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const needsBranch = accountType === 'Employee';
  // Sub-branches are kept per branch by the shared cache, so going back to a branch already seen does not ask again.
  const { data: availableSubBranches = [] } = useSubBranches(departmentId, { enabled: needsBranch });

  async function handleSave(e: React.FormEvent) {
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

    setSaving(true);
    try {
      await usersApi.updateUser(user.id, {
        firstName,
        lastName,
        role: accountType,
        companyId: accountType === 'Client' ? companyId : undefined,
        departmentId: needsBranch ? departmentId : undefined,
        subBranchId: needsBranch ? subBranchId || undefined : undefined,
      });
      onSaved();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not save changes.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={handleSave} className="mt-5 grid grid-cols-1 gap-4 border-t border-slate-100 pt-5 sm:grid-cols-2">
      <Field>
        <Label>First name</Label>
        <Input required maxLength={25} value={firstName} onChange={(e) => setFirstName(e.target.value)} />
      </Field>
      <Field>
        <Label>Last name</Label>
        <Input required maxLength={30} value={lastName} onChange={(e) => setLastName(e.target.value)} />
      </Field>
      <Field>
        <Label>Account type</Label>
        <Select value={accountType} onChange={(e) => setAccountType(e.target.value as UserRole)}>
          <option value="Client">Client</option>
          <option value="Employee">Employee</option>
          <option value="Admin">Admin</option>
        </Select>
      </Field>
      {accountType === 'Client' && (
        <Field>
          <Label>Company</Label>
          <Select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            <option value="">Select a company…</option>
            {companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </Field>
      )}
      {needsBranch && (
        <Field>
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
        </Field>
      )}
      {needsBranch && availableSubBranches.length > 0 && (
        <Field>
          <Label>Sub-branch</Label>
          <Select value={subBranchId} onChange={(e) => setSubBranchId(e.target.value)}>
            <option value="">Select a sub-branch…</option>
            {availableSubBranches.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
        </Field>
      )}
      <div className="sm:col-span-2">
        <ErrorText>{error}</ErrorText>
        <div className="flex gap-2">
          <Button type="submit" disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </Button>
          <Button type="button" variant="secondary" onClick={onCancel}>
            Cancel
          </Button>
        </div>
      </div>
    </form>
  );
}
