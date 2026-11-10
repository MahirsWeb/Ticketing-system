import type { CreatedUserResponse, UserRole } from '../../types';
import { Button, Card, ErrorText, Field, Input, Label, Select } from '../../components/ui';
import { useCreateAccount } from './useCreateAccount';
import { useLanguage } from '../../i18n/LanguageContext';

/** The form for a new account. An administrator can make any kind; anybody else only clients. */
export function CreateAccountForm({
  isAdmin,
  preselectedCompanyId,
  onCreated,
}: {
  isAdmin: boolean;
  preselectedCompanyId: string | null;
  onCreated: (created: CreatedUserResponse) => void;
}) {
  const { t } = useLanguage();
  const form = useCreateAccount({ preselectedCompanyId, onCreated });
  const { values, set } = form;

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">{t('createacc_title')}</h2>
      <form onSubmit={form.submit} className="grid grid-cols-2 gap-4">
        <Field>
          <Label>{t('common_firstName')}</Label>
          <Input required maxLength={25} value={values.firstName} onChange={(e) => set.firstName(e.target.value)} />
        </Field>
        <Field>
          <Label>{t('common_lastName')}</Label>
          <Input required maxLength={30} value={values.lastName} onChange={(e) => set.lastName(e.target.value)} />
        </Field>
        <Field>
          <Label>{t('common_email')}</Label>
          <Input type="email" required maxLength={100} value={values.email} onChange={(e) => set.email(e.target.value)} />
        </Field>
        <Field>
          <Label>{t('common_accountType')}</Label>
          <Select value={values.accountType} onChange={(e) => set.accountType(e.target.value as UserRole)}>
            <option value="Client">{t('role_Client')}</option>
            {isAdmin && (
              <>
                <option value="Employee">{t('role_Employee')}</option>
                <option value="Admin">{t('role_Admin')}</option>
              </>
            )}
          </Select>
        </Field>
        {form.needsCompany && (
          <Field>
            <Label>{t('common_company')}</Label>
            <Select value={values.companyId} onChange={(e) => set.companyId(e.target.value)}>
              <option value="">{t('common_selectCompany')}</option>
              {(form.companies.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
            {form.companies.isError && <ErrorText>{t('common_companiesLoadFail')}</ErrorText>}
          </Field>
        )}
        {form.needsBranch && (
          <Field>
            <Label>{t('detail_branch')}</Label>
            <Select value={values.departmentId} onChange={(e) => set.department(e.target.value)}>
              <option value="">{t('newTicket_selectBranchOption')}</option>
              {(form.departments.data ?? []).map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name} ({d.email})
                </option>
              ))}
            </Select>
            {form.departments.isError && <ErrorText>{t('filter_branchesLoadError')}</ErrorText>}
          </Field>
        )}
        {form.needsBranch && (form.subBranchChoices.length > 0 || form.subBranches.isError) && (
          <Field>
            <Label>{t('detail_subBranch')}</Label>
            <Select value={values.subBranchId} onChange={(e) => set.subBranchId(e.target.value)}>
              <option value="">{t('transfer_subOption')}</option>
              {form.subBranchChoices.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
            {form.subBranches.isError && <ErrorText>{t('common_subBranchesLoadFail')}</ErrorText>}
          </Field>
        )}
        <div className="col-span-2">
          <ErrorText>{form.error}</ErrorText>
          <Button type="submit" disabled={form.loading}>
            {form.loading ? t('onbehalf_creating') : t('createacc_title')}
          </Button>
        </div>
      </form>
    </Card>
  );
}
