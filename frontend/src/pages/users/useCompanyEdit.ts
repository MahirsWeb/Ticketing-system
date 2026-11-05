import { useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { messageOf } from '../../lib/messageOf';
import type { CompanyItemFull } from '../../types';

/**
 * Editing the details of a company: what is written in the fields, and the saving. What is sent has no spaces around it, and an address or a
 * contact of nothing but spaces is not sent. Switching a company on or off is asked about first. After a save the fields show what was saved
 * (as the server has it), and when the editing is cancelled they show what the company has.
 */
export function useCompanyEdit(company: CompanyItemFull, onClose: () => void) {
  const [name, setName] = useState(company.name);
  const [address, setAddress] = useState(company.address ?? '');
  const [contactInfo, setContactInfo] = useState(company.contactInfo ?? '');
  const [isActive, setIsActive] = useState(company.isActive);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [confirming, setConfirming] = useState(false);

  function show(c: CompanyItemFull) {
    setName(c.name);
    setAddress(c.address ?? '');
    setContactInfo(c.contactInfo ?? '');
    setIsActive(c.isActive);
    setError(null);
  }

  async function save() {
    const cleanName = name.trim();
    if (!cleanName) return;
    setSaving(true);
    setError(null);
    try {
      const updated = await lookupsApi.updateCompany(company.id, cleanName, address.trim() || undefined, contactInfo.trim() || undefined, isActive);
      show(updated);
      onClose();
    } catch (err) {
      setError(messageOf(err, 'Could not save changes.'));
    } finally {
      setSaving(false);
      // The question is answered either way: if it did not work the reason is in the form, which the question would be covering.
      setConfirming(false);
    }
  }

  function requestSave() {
    if (!name.trim()) return;
    if (isActive !== company.isActive) {
      setConfirming(true);
      return;
    }
    void save();
  }

  function cancel() {
    show(company);
    onClose();
  }

  return {
    values: { name, address, contactInfo, isActive },
    set: { name: setName, address: setAddress, contactInfo: setContactInfo, isActive: setIsActive },
    error,
    saving,
    confirming,
    dismissQuestion: () => setConfirming(false),
    requestSave,
    save,
    cancel,
  };
}

export type CompanyEdit = ReturnType<typeof useCompanyEdit>;
