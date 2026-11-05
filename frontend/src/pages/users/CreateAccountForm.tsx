import type { CreatedUserResponse, UserRole } from '../../types';
import { Button, Card, ErrorText, Field, Input, Label, Select } from '../../components/ui';
import { useCreateAccount } from './useCreateAccount';

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
  const form = useCreateAccount({ preselectedCompanyId, onCreated });
  const { values, set } = form;

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">Create account</h2>
      <form onSubmit={form.submit} className="grid grid-cols-2 gap-4">
        <Field>
          <Label>First name</Label>
          <Input required maxLength={25} value={values.firstName} onChange={(e) => set.firstName(e.target.value)} />
        </Field>
        <Field>
          <Label>Last name</Label>
          <Input required maxLength={30} value={values.lastName} onChange={(e) => set.lastName(e.target.value)} />
        </Field>
        <Field>
          <Label>Email</Label>
          <Input type="email" required maxLength={100} value={values.email} onChange={(e) => set.email(e.target.value)} />
        </Field>
        <Field>
          <Label>Account type</Label>
          <Select value={values.accountType} onChange={(e) => set.accountType(e.target.value as UserRole)}>
            <option value="Client">Client</option>
            {isAdmin && (
              <>
                <option value="Employee">Employee</option>
                <option value="Admin">Admin</option>
              </>
            )}
          </Select>
        </Field>
        {form.needsCompany && (
          <Field>
            <Label>Company</Label>
            <Select value={values.companyId} onChange={(e) => set.companyId(e.target.value)}>
              <option value="">Select a company…</option>
              {(form.companies.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
            {form.companies.isError && <ErrorText>The list of companies could not be loaded.</ErrorText>}
          </Field>
        )}
        {form.needsBranch && (
          <Field>
            <Label>Branch</Label>
            <Select value={values.departmentId} onChange={(e) => set.department(e.target.value)}>
              <option value="">Select a branch…</option>
              {(form.departments.data ?? []).map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name} ({d.email})
                </option>
              ))}
            </Select>
            {form.departments.isError && <ErrorText>The list of branches could not be loaded.</ErrorText>}
          </Field>
        )}
        {form.needsBranch && (form.subBranchChoices.length > 0 || form.subBranches.isError) && (
          <Field>
            <Label>Sub-branch</Label>
            <Select value={values.subBranchId} onChange={(e) => set.subBranchId(e.target.value)}>
              <option value="">Select a sub-branch…</option>
              {form.subBranchChoices.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
            {form.subBranches.isError && <ErrorText>The list of sub-branches could not be loaded.</ErrorText>}
          </Field>
        )}
        <div className="col-span-2">
          <ErrorText>{form.error}</ErrorText>
          <Button type="submit" disabled={form.loading}>
            {form.loading ? 'Creating…' : 'Create account'}
          </Button>
        </div>
      </form>
    </Card>
  );
}
