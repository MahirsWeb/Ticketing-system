import clsx from 'clsx';
import { Button, Card, ErrorText, Field, Input, Label } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { CompanyEdit } from './useCompanyEdit';
import { useLanguage } from '../../i18n/LanguageContext';

const NAME_MAX = 120;
const ADDRESS_MAX = 100;
const CONTACT_MAX = 120;

export const EDIT_PANEL_ID = 'company-edit-panel';

/** The form for the details of the company. Folded away while closed: out of reach of the keyboard and of a screen reader, not only invisible. */
export function CompanyEditPanel({ edit, open }: { edit: CompanyEdit; open: boolean }) {
  const { t } = useLanguage();
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
                <Label>{t('common_name')}</Label>
                <Input value={values.name} maxLength={NAME_MAX} onChange={(e) => set.name(e.target.value)} />
              </Field>
              <Field>
                <Label>{t('compedit_address')}</Label>
                <Input value={values.address} maxLength={ADDRESS_MAX} onChange={(e) => set.address(e.target.value)} />
              </Field>
              <Field>
                <Label>{t('compedit_contact')}</Label>
                <Input value={values.contactInfo} maxLength={CONTACT_MAX} onChange={(e) => set.contactInfo(e.target.value)} />
              </Field>
              <label className="flex items-center gap-2 text-sm text-slate-700">
                <input type="checkbox" checked={values.isActive} onChange={(e) => set.isActive(e.target.checked)} />
                {t('common_active')}
              </label>
              <label className="flex items-center gap-2 text-sm text-slate-700 md:col-span-2">
                <input type="checkbox" checked={values.aiIndexing} onChange={(e) => set.aiIndexing(e.target.checked)} />
                {t('compedit_aiIndexing')}
              </label>
              <div className="md:col-span-3">
                <ErrorText>{edit.error}</ErrorText>
                <div className="flex gap-2">
                  <Button onClick={edit.requestSave} disabled={edit.saving || !values.name.trim()}>
                    {edit.saving ? t('common_saving') : t('common_save')}
                  </Button>
                  <Button variant="secondary" onClick={edit.cancel}>
                    {t('common_cancel')}
                  </Button>
                </div>
              </div>
            </div>
          </Card>
        </div>
      </div>

      {edit.confirming && (
        <ConfirmDialog
          title={values.isActive ? t('compedit_reactivateTitle') : t('compedit_deactivateTitle')}
          message={
            values.isActive ? t('compedit_reactivateMsg', { name: values.name }) : t('compedit_deactivateMsg', { name: values.name })
          }
          confirmLabel={values.isActive ? t('compedit_reactivate') : t('compedit_deactivate')}
          danger={!values.isActive}
          onConfirm={edit.save}
          onCancel={edit.dismissQuestion}
        />
      )}
    </>
  );
}
