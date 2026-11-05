import clsx from 'clsx';
import { Button, Card, ErrorText, Field, Input, Label } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { CompanyEdit } from './useCompanyEdit';

const NAME_MAX = 120;
const ADDRESS_MAX = 100;
const CONTACT_MAX = 120;

export const EDIT_PANEL_ID = 'company-edit-panel';

/** The form for the details of the company. Folded away while closed: out of reach of the keyboard and of a screen reader, not only invisible. */
export function CompanyEditPanel({ edit, open }: { edit: CompanyEdit; open: boolean }) {
  const { values, set } = edit;
  return (
    <>
      <div
        id={EDIT_PANEL_ID}
        className={clsx('grid transition-all duration-200 ease-out', open ? 'grid-rows-[1fr] opacity-100' : 'grid-rows-[0fr] opacity-0')}
        inert={!open}
        aria-hidden={open ? undefined : true}
      >
        <div className="overflow-hidden">
          <Card className="p-5">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              <Field>
                <Label>Name</Label>
                <Input value={values.name} maxLength={NAME_MAX} onChange={(e) => set.name(e.target.value)} />
              </Field>
              <Field>
                <Label>Address</Label>
                <Input value={values.address} maxLength={ADDRESS_MAX} onChange={(e) => set.address(e.target.value)} />
              </Field>
              <Field>
                <Label>Contact info</Label>
                <Input value={values.contactInfo} maxLength={CONTACT_MAX} onChange={(e) => set.contactInfo(e.target.value)} />
              </Field>
              <label className="flex items-center gap-2 text-sm text-slate-700">
                <input type="checkbox" checked={values.isActive} onChange={(e) => set.isActive(e.target.checked)} />
                Active
              </label>
              <div className="md:col-span-3">
                <ErrorText>{edit.error}</ErrorText>
                <div className="flex gap-2">
                  <Button onClick={edit.requestSave} disabled={edit.saving || !values.name.trim()}>
                    {edit.saving ? 'Saving…' : 'Save'}
                  </Button>
                  <Button variant="secondary" onClick={edit.cancel}>
                    Cancel
                  </Button>
                </div>
              </div>
            </div>
          </Card>
        </div>
      </div>

      {edit.confirming && (
        <ConfirmDialog
          title={values.isActive ? 'Reactivate this company?' : 'Deactivate this company?'}
          message={
            values.isActive
              ? `"${values.name}" will become active again and reappear in the company picker when creating a new client account.`
              : `Existing clients and tickets for "${values.name}" are completely unaffected — nothing is deleted or changed for them. Deactivating only removes this company from the picker when creating a new client account.`
          }
          confirmLabel={values.isActive ? 'Reactivate' : 'Deactivate'}
          danger={!values.isActive}
          onConfirm={edit.save}
          onCancel={edit.dismissQuestion}
        />
      )}
    </>
  );
}
