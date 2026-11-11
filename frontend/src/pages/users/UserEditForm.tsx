import { useState } from 'react';
import { usersApi } from '../../api/users';
import { useCompanies, useDepartments, useSubBranches } from '../../api/lookupQueries';
import { Button, ErrorText, Field, Input, Label, Select } from '../../components/ui';
import type { UserListItemDto, UserRole } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';

/**
 * The form for changing the name and the place of an account. It starts from what the account is when it is drawn, so closing it and opening
 * it again forgets what was typed (the page draws a new one each time).
 */
export function UserEditForm({ user, onSaved, onCancel }: { user: UserListItemDto; onSaved: () => void; onCancel: () => void }) {
  const { t } = useLanguage();
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
      setError(t('edit_selectCompany'));
      return;
    }
    if (needsBranch && !departmentId) {
      setError(t('edit_selectBranch'));
      return;
    }
    if (needsBranch && availableSubBranches.length > 0 && !subBranchId) {
      setError(t('edit_selectSub'));
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
      setError(err?.response?.data?.message ?? t('edit_saveFail'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={handleSave} className="mt-5 grid grid-cols-1 gap-4 border-t border-slate-100 pt-5 sm:grid-cols-2">
      <Field>
        <Label>{t('common_firstName')}</Label>
        <Input required maxLength={25} value={firstName} onChange={(e) => setFirstName(e.target.value)} />
      </Field>
      <Field>
        <Label>{t('common_lastName')}</Label>
        <Input required maxLength={30} value={lastName} onChange={(e) => setLastName(e.target.value)} />
      </Field>
      <Field>
        <Label>{t('common_accountType')}</Label>
        <Select value={accountType} onChange={(e) => setAccountType(e.target.value as UserRole)}>
          <option value="Client">{t('role_Client')}</option>
          <option value="Employee">{t('role_Employee')}</option>
          <option value="Admin">{t('role_Admin')}</option>
        </Select>
      </Field>
      {accountType === 'Client' && (
        <Field>
          <Label>{t('common_company')}</Label>
          <Select value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            <option value="">{t('common_selectCompany')}</option>
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
          <Label>{t('detail_branch')}</Label>
          <Select
            value={departmentId}
            onChange={(e) => {
              setDepartmentId(e.target.value);
              setSubBranchId('');
            }}
          >
            <option value="">{t('newTicket_selectBranchOption')}</option>
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
          <Label>{t('detail_subBranch')}</Label>
          <Select value={subBranchId} onChange={(e) => setSubBranchId(e.target.value)}>
            <option value="">{t('transfer_subOption')}</option>
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
            {saving ? t('common_saving') : t('common_save')}
          </Button>
          <Button type="button" variant="secondary" onClick={onCancel}>
            {t('common_cancel')}
          </Button>
        </div>
      </div>
    </form>
  );
}
