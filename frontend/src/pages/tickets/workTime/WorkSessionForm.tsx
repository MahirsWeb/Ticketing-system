import { Button, Label } from '../../../components/ui';
import { TimeInput } from './TimeInput';
import type { useWorkTimeForm } from './useWorkTimeForm';

const dateInputClass =
  'rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600';

/** The form that adds a session by the main assignee, or changes the session that was picked. Enter sends it from any field. */
export function WorkSessionForm({ form, workerName }: { form: ReturnType<typeof useWorkTimeForm>; workerName: string | null }) {
  const { editingId, saving } = form;

  return (
    <form onSubmit={form.submit}>
      <div className="mb-1 text-xs text-slate-600">{editingId ? 'Change this session' : `Log work by ${workerName}`}</div>
      <div className="flex flex-wrap items-end gap-4">
        <div role="group" aria-label="Started">
          <Label>Started</Label>
          <div className="flex items-center gap-1.5">
            <input type="date" aria-label="Started date" value={form.startDate} onChange={(e) => form.setStartDate(e.target.value)} className={dateInputClass} />
            <TimeInput label="Started time" value={form.startTime} onChange={form.setStartTime} />
          </div>
        </div>
        <div role="group" aria-label="Ended">
          <Label>Ended (empty = still working)</Label>
          <div className="flex items-center gap-1.5">
            <input type="date" aria-label="Ended date" value={form.endDate} onChange={(e) => form.setEndDate(e.target.value)} className={dateInputClass} />
            <TimeInput label="Ended time" value={form.endTime} onChange={form.setEndTime} />
          </div>
        </div>
        <Button type="submit" variant="secondary" className="text-xs" disabled={saving}>
          {saving ? 'Saving…' : editingId ? 'Save changes' : 'Add session'}
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
