import { Button, Label } from '../../../components/ui';
import { TimeInput } from './TimeInput';
import type { useWorkTimeForm } from './useWorkTimeForm';
import { useLanguage } from '../../../i18n/LanguageContext';

const dateInputClass =
  'rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600';

/** The form that adds a session by the main assignee, or changes the session that was picked. Enter sends it from any field. */
export function WorkSessionForm({ form, workerName }: { form: ReturnType<typeof useWorkTimeForm>; workerName: string | null }) {
  const { t } = useLanguage();
  const { editingId, saving } = form;

  return (
    <form onSubmit={form.submit}>
      <div className="mb-1 text-xs text-slate-600">{editingId ? t('worktime_changeSession') : t('worktime_logBy', { name: workerName ?? '' })}</div>
      <div className="flex flex-wrap items-end gap-4">
        <div role="group" aria-label={t('worktime_startedAria')}>
          <Label>{t('worktime_started')}</Label>
          <div className="flex items-center gap-1.5">
            <input type="date" aria-label={t('worktime_startedDate')} value={form.startDate} onChange={(e) => form.setStartDate(e.target.value)} className={dateInputClass} />
            <TimeInput label={t('worktime_startedTime')} value={form.startTime} onChange={form.setStartTime} />
          </div>
        </div>
        <div role="group" aria-label={t('worktime_endedAria')}>
          <Label>{t('worktime_ended')}</Label>
          <div className="flex items-center gap-1.5">
            <input type="date" aria-label={t('worktime_endedDate')} value={form.endDate} onChange={(e) => form.setEndDate(e.target.value)} className={dateInputClass} />
            <TimeInput label={t('worktime_endedTime')} value={form.endTime} onChange={form.setEndTime} />
          </div>
        </div>
        <Button type="submit" variant="secondary" className="text-xs" disabled={saving}>
          {saving ? t('common_saving') : editingId ? t('common_saveChanges') : 'Add session'}
        </Button>
        {editingId && (
          <Button type="button" variant="ghost" className="text-xs" disabled={saving} onClick={form.cancelEdit}>
            Cancel
          </Button>
        )}
        {form.draftDuration && <span className="text-xs font-medium text-slate-600">Duration: {form.draftDuration}</span>}
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Time is logged for the main assignee only. People added as extra help are listed on the ticket but log no time of their own.
      </p>
    </form>
  );
}
