import { useEffect, useState } from 'react';
import { usersApi, type NotificationPreference } from '../../api/users';
import { Button, Card, ErrorText } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';
import { messageOf } from '../../lib/messageOf';

/**
 * The e-mails about tickets and tasks that a person can switch off, whoever they are (the kinds on the list are the ones their role is sent). What the bell
 * shows, and the e-mails about the account itself, are not on it: those are not a choice.
 */
export default function NotificationPreferencesPage() {
  const { t } = useLanguage();
  const [items, setItems] = useState<NotificationPreference[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    let current = true;
    usersApi
      .getMyNotificationPreferences()
      .then((loaded) => current && setItems(loaded))
      .catch((err) => current && setError(messageOf(err, t('notifpref_loadFail'))));
    return () => {
      current = false;
    };
  }, [t]);

  function toggle(type: string, email: boolean) {
    setSaved(false);
    setItems((prev) => prev?.map((p) => (p.type === type ? { ...p, email } : p)) ?? prev);
  }

  async function save() {
    if (!items) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      setItems(await usersApi.setMyNotificationPreferences(items));
      setSaved(true);
    } catch (err) {
      setError(messageOf(err, t('notifpref_saveFail')));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-2xl space-y-4 px-4 py-6">
      <h1 className="text-xl font-semibold text-slate-900">{t('notifpref_title')}</h1>
      <p className="text-sm text-slate-600">{t('notifpref_intro')}</p>
      <Card className="space-y-3 p-5">
        {items === null && !error && <p className="text-sm text-slate-600">…</p>}
        {items?.map((p) => (
          <label key={p.type} className="flex items-center gap-3 text-sm text-slate-800">
            <input type="checkbox" checked={p.email} onChange={(e) => toggle(p.type, e.target.checked)} />
            {t(`notifpref_${p.type}` as TranslationKey)}
          </label>
        ))}
        <ErrorText>{error}</ErrorText>
        {saved && (
          <p role="status" className="text-sm text-green-700">
            {t('notifpref_saved')}
          </p>
        )}
        {items && (
          <Button onClick={save} disabled={saving}>
            {saving ? t('common_saving') : t('common_save')}
          </Button>
        )}
      </Card>
    </div>
  );
}
