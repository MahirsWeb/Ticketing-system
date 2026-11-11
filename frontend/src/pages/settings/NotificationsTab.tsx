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
import { useLanguage } from '../../i18n/LanguageContext';

/** The form is drawn from the settings the server has, and only from them: made-up starting values that could be saved over the real ones are never shown. */
function OverdueForm({ initial }: { initial: OverdueNotificationSettingsDto }) {
  const { t } = useLanguage();
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
      setError(messageOf(err, t('notif_saveFail')));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">{t('notif_overdueTitle')}</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-600">{t('notif_overdueIntro')}</p>
      <label className="mb-3 flex items-center gap-2 text-sm text-slate-700">
        <input type="checkbox" checked={notifyOnSlaBreach} onChange={(e) => setNotifyOnSlaBreach(e.target.checked)} />
        {t('notif_slaCheckbox')}
      </label>
      <Field>
        <Label>{t('notif_overdueDaysLabel')}</Label>
        <Input
          type="number"
          min={1}
          max={365}
          placeholder={t('notif_overdueDaysPlaceholder')}
          value={manualOverdueDays}
          onChange={(e) => setManualOverdueDays(e.target.value)}
          className="max-w-[8rem]"
        />
      </Field>
      <div className="mt-4">
        <ErrorText>{error}</ErrorText>
        <SuccessText>{saved ? t('notif_saved') : null}</SuccessText>
        <Button onClick={handleSave} disabled={saving}>
          {saving ? t('common_saving') : t('common_save')}
        </Button>
      </div>
    </Panel>
  );
}

/** When to warn about overdue tickets, and the upkeep of what the AI assistant reads. */
export function NotificationsTab() {
  const { t } = useLanguage();
  const settings = useOverdueSettings();

  return (
    <div className="space-y-6">
      {settings.isError && (
        <Panel className="max-w-lg">
          <ErrorText>{t('notif_settingsLoadFail')}</ErrorText>
        </Panel>
      )}
      {settings.isSuccess && <OverdueForm initial={settings.data} />}

      <KnowledgeBaseMaintenancePanel />
      <KnowledgeBaseDocumentsPanel />
    </div>
  );
}
