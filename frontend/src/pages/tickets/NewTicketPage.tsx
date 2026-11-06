import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ticketsApi } from '../../api/tickets';
import { useDepartments } from '../../api/lookupQueries';
import { Button, Card, ErrorText, Input, Label, Select, Field } from '../../components/ui';
import { RichTextEditor } from '../../components/RichTextEditor';
import { newIdempotencyKey } from '../../lib/idempotency';
import { isBlankHtml } from '../../lib/htmlText';

export default function NewTicketPage() {
  const navigate = useNavigate();
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const { data: departments = [] } = useDepartments();
  const [files, setFiles] = useState<File[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  // One key per visit to this form: a retry of the same submission gets the existing ticket back, not a duplicate.
  const [idempotencyKey] = useState(newIdempotencyKey);

  function addFiles(newFiles: File[]) {
    setFiles((prev) => [...prev, ...newFiles]);
  }

  function removeFile(index: number) {
    setFiles((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!title.trim() || isBlankHtml(description)) {
      setError('Title and description are required.');
      return;
    }
    if (!departmentId) {
      setError('Please select a branch to send this ticket to.');
      return;
    }
    setLoading(true);
    try {
      const ticket = await ticketsApi.create(title, description, departmentId, idempotencyKey);
      if (files.length > 0) {
        await ticketsApi.uploadAttachments(ticket.id, files);
      }
      navigate(`/tickets/${ticket.id}`);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not submit your ticket.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="mb-4 text-xl font-bold text-slate-900">Submit a ticket</h1>
      <Card className="p-6">
        <form onSubmit={handleSubmit} className="space-y-4">
          <Field>
            <Label>Title *</Label>
            <Input required value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Short summary of the issue" />
          </Field>
          <Field>
            <Label>Branch *</Label>
            <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
              <option value="">Select a branch…</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name} ({d.email})
                </option>
              ))}
            </Select>
          </Field>
          <div>
            <Label>Description *</Label>
            <RichTextEditor
                ariaLabel="Description"
              value={description}
              onChange={setDescription}
              onFilesSelected={addFiles}
              placeholder="Describe the issue in detail… (you can also paste or attach a screenshot)"
            />
            {files.length > 0 && (
              <ul className="mt-2 flex flex-wrap gap-2">
                {files.map((f, i) => (
                  <li key={`${f.name}-${i}`} className="flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-600">
                    <span aria-hidden>📎</span> {f.name}
                    <button type="button" onClick={() => removeFile(i)} className="ml-1 font-bold text-slate-600 hover:text-red-600">
                      ×
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" disabled={loading}>
            {loading ? 'Submitting…' : 'Submit ticket'}
          </Button>
        </form>
      </Card>
    </div>
  );
}
