import { Button, Card, ErrorText, Field, Input, Label, Select } from '../../components/ui';
import { RichTextEditor } from '../../components/RichTextEditor';
import { DueDateField, SelectField } from './ticketFields';
import { PRIORITIES, SOURCES } from './ticketOptions';
import type { OnBehalfTicket } from './useOnBehalfTicket';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';

/** The second step: describe the issue and say where the ticket goes. All of it is drawn from the model, which keeps what was typed. */
export function OnBehalfTicketFields({ model }: { model: OnBehalfTicket }) {
  const { t } = useLanguage();
  const { values, set, lists, subBranches, employeesByDepartment } = model;

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-600">{t('onbehalf_heading')}</h2>
      <form onSubmit={model.submit} className="space-y-4">
        {lists.failed && <ErrorText>Some of the lists could not be loaded. Reload the page to try again.</ErrorText>}
        <Field>
          <Label>{t('newTicket_titleLabel')}</Label>
          <Input required value={values.title} onChange={(e) => set.setTitle(e.target.value)} placeholder={t('newTicket_titlePlaceholder')} />
        </Field>
        <div>
          <Label>{t('newTicket_descLabel')}</Label>
          <RichTextEditor
            ariaLabel="Description"
            value={values.description}
            onChange={set.setDescription}
            onFilesSelected={model.addFiles}
            placeholder={t('onbehalf_descPlaceholder')}
          />
          {values.files.length > 0 && (
            <ul className="mt-2 flex flex-wrap gap-2">
              {values.files.map((f, i) => (
                <li key={`${f.name}-${i}`} className="flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-600">
                  <span aria-hidden>📎</span> {f.name}
                  <button type="button" onClick={() => model.removeFile(i)} className="ml-1 font-bold text-slate-600 hover:text-red-600">
                    ×
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="grid grid-cols-2 gap-4">
          <SelectField label={t('form_ticketSource')} value={values.source} onChange={(v) => set.setSource(v as typeof values.source)} options={SOURCES.map((s) => ({ value: s, label: t(`source_${s}` as TranslationKey) }))} />
          <SelectField label={t('detail_helpTopic')} value={values.helpTopicId} onChange={set.setHelpTopicId} options={lists.helpTopics.map((t) => ({ value: t.id, label: t.name }))} />
          {model.isAdmin ? (
            <SelectField
              label={t('detail_branch')}
              value={values.departmentId}
              onChange={set.changeDepartment}
              placeholder={t('newTicket_selectBranchOption')}
              options={lists.departments.map((d) => ({ value: d.id, label: d.name }))}
            />
          ) : (
            <Field>
              <Label>{t('detail_branch')}</Label>
              <div className="rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">{model.ownBranchName ?? t('onbehalf_noBranchAssigned')}</div>
            </Field>
          )}
          {subBranches.length > 0 && (
            <SelectField
              label={t('detail_subBranch')}
              value={values.subBranchId}
              onChange={set.setSubBranchId}
              placeholder={t('transfer_subOption')}
              options={subBranches.map((s) => ({ value: s.id, label: s.name }))}
            />
          )}
          <SelectField
            label={t('detail_slaPlan')}
            value={values.slaPlanId}
            onChange={set.setSlaPlanId}
            options={lists.slaPlans.map((s) => ({ value: s.id, label: `${s.name} (${s.resolutionTimeHours}h resolution)` }))}
          />
          <SelectField label={t('table_priority')} value={values.priority} onChange={(v) => set.setPriority(v as typeof values.priority)} options={PRIORITIES.map((p) => ({ value: p, label: t(`priority_${p}` as TranslationKey) }))} />
          <DueDateField value={values.dueDate} onChange={set.setDueDate} />
          <Field>
            <Label>{t('form_assignTo')}</Label>
            <Select value={values.assignedToUserId} onChange={(e) => set.changeAssignee(e.target.value)}>
              <option value="">{t('form_selectAgent')}</option>
              {lists.departments.map((d) => {
                const deptEmployees = employeesByDepartment.get(d.id) ?? [];
                if (deptEmployees.length === 0) return null;
                return (
                  <optgroup key={d.id} label={d.name}>
                    {deptEmployees.map((e) => (
                      <option key={e.id} value={e.id}>
                        {e.firstName} {e.lastName}
                      </option>
                    ))}
                  </optgroup>
                );
              })}
            </Select>
            {model.staffFailed && <ErrorText>{t('form_staffFail')}</ErrorText>}
          </Field>
          <SelectField
            label={t('form_category')}
            value={values.categoryId}
            onChange={set.setCategoryId}
            placeholder={t('form_noCategory')}
            options={lists.categories.map((c) => ({ value: c.id, label: c.name }))}
          />
        </div>

        <ErrorText>{model.error}</ErrorText>
        <Button type="submit" disabled={model.loading}>
          {model.loading ? t('onbehalf_creating') : t('onbehalf_submit')}
        </Button>
      </form>
    </Card>
  );
}
