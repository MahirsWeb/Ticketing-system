import { useState } from 'react';
import { useAuthStore } from '../store/authStore';
import { usersApi } from '../api/users';
import { Button, Input } from './ui';
import { useLanguage } from '../i18n/LanguageContext';

export function PhoneNumberBanner() {
  const { t } = useLanguage();
  const user = useAuthStore((s) => s.user);
  const updateUser = useAuthStore((s) => s.updateUser);
  const [phone, setPhone] = useState('');
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);

  if (!user || user.phoneNumber || user.phoneNumberPrompted) return null;

  async function handleSave() {
    setSaving(true);
    try {
      await usersApi.setMyPhone(phone);
      updateUser({ phoneNumber: phone, phoneNumberPrompted: true });
    } finally {
      setSaving(false);
    }
  }

  async function handleSkip() {
    await usersApi.skipPhonePrompt();
    updateUser({ phoneNumberPrompted: true });
  }

  return (
    <div className="flex flex-wrap items-center gap-3 border-b border-amber-200 bg-amber-50 px-6 py-2.5 text-sm text-amber-900">
      {!editing ? (
        <>
          <span>{t('phone_prompt')}</span>
          <Button variant="secondary" className="py-1" onClick={() => setEditing(true)}>
            {t('phone_add')}
          </Button>
          <button className="text-amber-700 underline" onClick={handleSkip}>
            {t('phone_skip')}
          </button>
        </>
      ) : (
        <>
          <Input
            className="w-48"
            placeholder="+387 6X XXX XXX"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
          />
          <Button className="py-1" onClick={handleSave} disabled={saving || !phone}>
            {t('common_save')}
          </Button>
          <button className="text-amber-700 underline" onClick={() => setEditing(false)}>
            {t('common_cancel')}
          </button>
        </>
      )}
    </div>
  );
}
