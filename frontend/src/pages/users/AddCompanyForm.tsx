import { useState } from 'react';
import { lookupsApi } from '../../api/lookups';
import { messageOf } from '../../lib/messageOf';
import { Button, Card, ErrorText, Field, Input, Label } from '../../components/ui';

/**
 * The form for a new company. What is written is sent without the spaces around it, and an address or a contact of nothing but spaces is not
 * sent. The lists of companies on screen are asked again by the API layer when one is added.
 */
export function AddCompanyForm() {
  const [name, setName] = useState('');
  const [address, setAddress] = useState('');
  const [contactInfo, setContactInfo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const cleanName = name.trim();
    if (!cleanName) {
      setError('Company name is required.');
      return;
    }
    setLoading(true);
    try {
      await lookupsApi.createCompany(cleanName, address.trim() || undefined, contactInfo.trim() || undefined);
      setName('');
      setAddress('');
      setContactInfo('');
    } catch (err) {
      setError(messageOf(err, 'Could not create the company.'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-bold uppercase tracking-wide text-slate-500">Add a company</h2>
      <form onSubmit={handleCreate} className="grid grid-cols-3 gap-4">
        <Field>
          <Label>Name</Label>
          <Input required maxLength={120} value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field>
          <Label>Address (optional)</Label>
          <Input maxLength={100} value={address} onChange={(e) => setAddress(e.target.value)} />
        </Field>
        <Field>
          <Label>Contact info (optional)</Label>
          <Input maxLength={120} value={contactInfo} onChange={(e) => setContactInfo(e.target.value)} />
        </Field>
        <div className="col-span-3">
          <ErrorText>{error}</ErrorText>
          <Button type="submit" disabled={loading}>
            {loading ? 'Adding…' : 'Add company'}
          </Button>
        </div>
      </form>
    </Card>
  );
}
