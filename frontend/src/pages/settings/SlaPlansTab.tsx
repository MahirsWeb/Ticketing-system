import { useMemo, useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { useSlaPlansAdmin } from '../../api/adminLookupQueries';
import { Button, ErrorText, Field, Input, Label, StatusPill } from '../../components/ui';
import type { SlaPlanItemFull } from '../../types';
import { LookupDialogs } from './LookupDialogs';
import { LIMITS, messageOf } from './settingsHelpers';
import { ListRow, ListStatus, Panel } from './settingsUi';
import { useLookupActions } from './useLookupActions';

const DEFAULT_HOURS = 72;

/** The SLA plans: how many hours a ticket has before it is overdue. A plan is not edited, only switched off and on, or deleted when no ticket uses it. */
export function SlaPlansTab() {
  const plans = useSlaPlansAdmin();
  const data = plans.data;
  const count = data?.length ?? 0;
  const [name, setName] = useState('');
  const [resolutionHours, setResolutionHours] = useState(DEFAULT_HOURS);
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const actions = useLookupActions<SlaPlanItemFull>({
    noun: 'SLA plan',
    setActive: (p, isActive) => lookupsApi.updateSlaPlan(p.id, p.name, p.resolutionTimeHours, isActive),
    remove: (p) => lookupsApi.deleteSlaPlan(p.id),
  });

  const filtered = useMemo(() => {
    const items = data ?? [];
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((i) => i.name.toLowerCase().includes(q));
  }, [data, search]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) {
      setError('SLA plan name is required.');
      return;
    }
    try {
      await lookupsApi.createSlaPlan(name.trim(), resolutionHours);
      setName('');
      setResolutionHours(DEFAULT_HOURS);
    } catch (err) {
      setError(messageOf(err, 'Could not add SLA plan.'));
    }
  }

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[300px_1fr]">
      <Panel className="h-fit">
        <h2 className="text-sm font-bold text-slate-800">Add new SLA plan</h2>
        <form onSubmit={handleCreate} className="mt-4 space-y-3">
          <Field>
            <Label>Name</Label>
            <Input
              placeholder="e.g. Standard"
              value={name}
              maxLength={LIMITS.slaName}
              onChange={(e) => {
                setName(e.target.value);
                setError(null);
              }}
            />
          </Field>
          <Field>
            <Label>Resolution time (hours)</Label>
            <Input
              type="number"
              min={1}
              value={resolutionHours}
              onChange={(e) => {
                setResolutionHours(Number(e.target.value));
                setError(null);
              }}
            />
          </Field>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" className="w-full" disabled={!name.trim()}>
            + Add SLA Plan
          </Button>
        </form>
      </Panel>

      <Panel>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold text-slate-800">Existing SLA plans{plans.isSuccess ? ` (${count})` : ''}</h2>
          <Input aria-label="Search SLA plans" placeholder="Search SLA plans…" value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        </div>
        <ErrorText>{actions.changeError}</ErrorText>
        <ListStatus loading={plans.isPending} failed={plans.isError} plural="SLA plans" />
        {plans.isSuccess && (
          <div className="divide-y divide-slate-100">
            {filtered.map((item) => (
              <ListRow key={item.id}>
                <span className="text-sm text-slate-800">
                  {item.name} <span className="text-xs text-slate-600">— {item.resolutionTimeHours}h resolution</span>
                </span>
                <div className="flex items-center gap-3 text-xs">
                  <StatusPill active={item.isActive} />
                  <button className="text-slate-600 hover:underline" onClick={() => actions.toggle(item)}>
                    {item.isActive ? 'Deactivate' : 'Reactivate'}
                  </button>
                  <button className="font-medium text-red-600 hover:underline" onClick={() => actions.askDelete(item)}>
                    Delete
                  </button>
                </div>
              </ListRow>
            ))}
            {filtered.length === 0 && <p className="py-8 text-center text-sm text-slate-600">No SLA plans found.</p>}
          </div>
        )}
      </Panel>

      <LookupDialogs
        actions={actions}
        remove={{
          title: (item) => `Delete "${item.name}"?`,
          message: () =>
            'This SLA plan will be permanently deleted. An SLA plan that tickets already use cannot be deleted (deactivate it instead), so nothing in the history of any ticket is lost.',
        }}
        deactivate={{
          title: (item) => `Deactivate "${item.name}"?`,
          message: (item) =>
            `Tickets currently using "${item.name}" keep it and are completely unaffected — nothing changes for existing tickets or statistics. This only removes it from the picker when opening a new ticket.`,
        }}
      />
    </div>
  );
}
