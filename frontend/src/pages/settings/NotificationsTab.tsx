import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { settingsApi } from '../../api/settings';
import { settingsKeys, useOverdueSettings } from '../../api/settingsQueries';
import { Button, ErrorText, Field, Input, Label, SuccessText } from '../../components/ui';
import type { OverdueNotificationSettingsDto } from '../../types';
import { KnowledgeBaseDocumentsPanel } from './KnowledgeBaseDocumentsPanel';
import { KnowledgeBaseMaintenancePanel } from './KnowledgeBaseMaintenancePanel';
import { messageOf } from './settingsHelpers';
import { Panel } from './settingsUi';

/** The form is drawn from the settings the server has, and only from them: made-up starting values that could be saved over the real ones are never shown. */
function OverdueForm({ initial }: { initial: OverdueNotificationSettingsDto }) {
  const queryClient = useQueryClient();
  const [notifyOnSlaBreach, setNotifyOnSlaBreach] = useState(initial.notifyOnSlaBreach);
  const [manualOverdueDays, setManualOverdueDays] = useState<string>(initial.manualOverdueDays?.toString() ?? '');
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const result = await settingsApi.updateOverdueNotifications(notifyOnSlaBreach, manualOverdueDays.trim() ? Number(manualOverdueDays) : null);
      queryClient.setQueryData(settingsKeys.overdue, result);
      setSaved(true);
    } catch (err) {
      setError(messageOf(err, 'Could not save settings.'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">Overdue ticket notifications</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-600">
        Emails every assignee once a ticket first becomes overdue. Each ticket is only notified once.
      </p>
      <label className="mb-3 flex items-center gap-2 text-sm text-slate-700">
        <input type="checkbox" checked={notifyOnSlaBreach} onChange={(e) => setNotifyOnSlaBreach(e.target.checked)} />
        Notify when a ticket passes its SLA due date
      </label>
      <Field>
        <Label>Also notify after this many days open (optional)</Label>
        <Input
          type="number"
          min={1}
          max={365}
          placeholder="e.g. 3"
          value={manualOverdueDays}
          onChange={(e) => setManualOverdueDays(e.target.value)}
          className="max-w-[8rem]"
        />
      </Field>
      <div className="mt-4">
        <ErrorText>{error}</ErrorText>
        <SuccessText>{saved ? 'Saved.' : null}</SuccessText>
        <Button onClick={handleSave} disabled={saving}>
          {saving ? 'Saving…' : 'Save'}
        </Button>
      </div>
    </Panel>
  );
}

/** When to warn about overdue tickets, and the upkeep of what the AI assistant reads. */
export function NotificationsTab() {
  const settings = useOverdueSettings();

  return (
    <div className="space-y-6">
      {settings.isError && (
        <Panel className="max-w-lg">
          <ErrorText>The settings could not be loaded. Try again in a moment.</ErrorText>
        </Panel>
      )}
      {settings.isSuccess && <OverdueForm initial={settings.data} />}

      <KnowledgeBaseMaintenancePanel />
      <KnowledgeBaseDocumentsPanel />
    </div>
  );
}
